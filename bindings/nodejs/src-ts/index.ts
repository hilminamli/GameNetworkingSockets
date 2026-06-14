import { EventEmitter } from 'node:events';
import * as path from 'node:path';

// Load the N-API addon. node-gyp-build picks the right artifact automatically:
//   1. a prebuilt binary under prebuilds/<platform>-<arch>/ (shipped in the package), or
//   2. a locally compiled build/Release/gns.node (dev / `npm run build:native`).
// The package root is one level up from dist/ (where this file lives at runtime).
const pkgRoot = path.join(__dirname, '..');
// eslint-disable-next-line @typescript-eslint/no-var-requires
const native = require('node-gyp-build')(pkgRoot) as NativeAddon;

// ── Native surface (matches src/addon.cpp) ──────────────────────────────────────
interface NativeMessage {
  conn: number;
  data: Buffer;
  flags: number;
}
type StatusEvent = 'connect' | 'disconnect';
type StatusCallback = (event: StatusEvent, conn: number, endReason: number, endDebug: string) => void;

interface NativeAddon {
  init(): boolean;
  kill(): void;
  createServer(port: number, onStatus: StatusCallback): number;
  createClient(onStatus: StatusCallback): number;
  connect(peerId: number, target: string): number;
  send(conn: number, data: Buffer, flags: number): number;
  poll(peerId: number): NativeMessage[];
  runCallbacks(): void;
  closeConnection(conn: number, reason?: number, debug?: string): boolean;
  getConnectionStatus(conn: number): { ping: number; packetLoss: number } | null;
  destroyPeer(peerId: number): void;
}

/** GNS send flags (k_nSteamNetworkingSend_*). */
export const SendType = {
  Unreliable: 0,
  NoNagle: 1,
  Reliable: 8,
  ReliableNoNagle: 9,
} as const;
export type SendType = (typeof SendType)[keyof typeof SendType];

// ── Library lifecycle ───────────────────────────────────────────────────────────
let initialized = false;

/** Initializes the GNS library. Idempotent. Called automatically by the first peer. */
export function init(): void {
  if (initialized) return;
  if (!native.init()) throw new Error('GameNetworkingSockets_Init failed');
  initialized = true;
}

/** Shuts down the GNS library. Call after destroying all peers. */
export function shutdown(): void {
  stopTick();
  native.kill();
  initialized = false;
}

// ── Central tick: one timer drives RunCallbacks + per-peer poll for all peers ────
const activePeers = new Set<PeerBase>();
let tickTimer: NodeJS.Timeout | null = null;
let tickIntervalMs = 16; // ~60Hz default

function startTick(): void {
  if (tickTimer) return;
  tickTimer = setInterval(() => {
    native.runCallbacks(); // fires status callbacks synchronously into each peer's onStatus
    for (const peer of activePeers) peer._drain();
  }, tickIntervalMs);
  // Don't keep the event loop alive solely for the tick.
  tickTimer.unref?.();
}

function stopTick(): void {
  if (tickTimer) {
    clearInterval(tickTimer);
    tickTimer = null;
  }
}

/** Sets how often (ms) the library polls for messages/callbacks. Default 16ms (~60Hz). */
export function setTickInterval(ms: number): void {
  tickIntervalMs = Math.max(1, ms);
  if (tickTimer) {
    stopTick();
    startTick();
  }
}

// ── Shared peer base ────────────────────────────────────────────────────────────
abstract class PeerBase extends EventEmitter {
  protected peerId = 0;
  protected destroyed = false;

  protected register(): void {
    activePeers.add(this);
    startTick();
  }

  /** @internal Drains pending messages and emits 'message'. Called by the central tick. */
  _drain(): void {
    if (this.destroyed) return;
    const msgs = native.poll(this.peerId);
    for (const m of msgs) this.emit('message', m.conn, m.data, m.flags);
  }

  /** Real-time ping (ms) and packet loss (0..1) for a connection, or null. */
  getStatus(conn: number): { ping: number; packetLoss: number } | null {
    return native.getConnectionStatus(conn);
  }

  destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    activePeers.delete(this);
    native.destroyPeer(this.peerId);
    if (activePeers.size === 0) stopTick();
  }
}

// ── Server ──────────────────────────────────────────────────────────────────────
/**
 * Listens for incoming connections. Auto-accepts and pools them.
 * Events:
 *   'connect'    (conn:number)
 *   'disconnect' (conn:number, endReason:number, endDebug:string)
 *   'message'    (conn:number, data:Buffer, flags:number)
 */
export class GnsServer extends PeerBase {
  readonly clients = new Set<number>();

  constructor(port: number) {
    super();
    init();
    this.peerId = native.createServer(port, (event, conn, endReason, endDebug) => {
      if (event === 'connect') {
        this.clients.add(conn);
        this.emit('connect', conn);
      } else {
        this.clients.delete(conn);
        this.emit('disconnect', conn, endReason, endDebug);
      }
    });
    this.register();
  }

  /** Sends to one client. */
  send(conn: number, data: Buffer, sendType: SendType = SendType.Reliable): number {
    return native.send(conn, data, sendType);
  }

  /** Sends to every connected client. */
  broadcast(data: Buffer, sendType: SendType = SendType.Reliable): void {
    for (const conn of this.clients) native.send(conn, data, sendType);
  }

  /** Disconnects a client. */
  kick(conn: number, reason = 0, debug?: string): void {
    native.closeConnection(conn, reason, debug);
    this.clients.delete(conn);
  }
}

// ── Client ──────────────────────────────────────────────────────────────────────
/**
 * Connects to a remote server. In a multi-server mesh, each node runs a GnsServer
 * and opens a GnsClient per peer it dials out to.
 * Events:
 *   'connect'    (conn:number)
 *   'disconnect' (conn:number, endReason:number, endDebug:string)
 *   'message'    (conn:number, data:Buffer, flags:number)
 */
export class GnsClient extends PeerBase {
  conn = 0;
  connected = false;

  constructor() {
    super();
    init();
    this.peerId = native.createClient((event, conn, endReason, endDebug) => {
      if (event === 'connect') {
        this.connected = true;
        this.emit('connect', conn);
      } else {
        this.connected = false;
        this.conn = 0;
        this.emit('disconnect', conn, endReason, endDebug);
      }
    });
    this.register();
  }

  /** Initiates a connection to "ip:port". Returns the connection handle (0 on failure). */
  connect(target: string): number {
    this.conn = native.connect(this.peerId, target);
    return this.conn;
  }

  /** Sends to the active server connection. */
  send(data: Buffer, sendType: SendType = SendType.Reliable): number {
    if (!this.conn) throw new Error('Not connected');
    return native.send(this.conn, data, sendType);
  }

  /** Closes the active connection. */
  disconnect(reason = 0, debug?: string): void {
    if (this.conn) native.closeConnection(this.conn, reason, debug);
    this.conn = 0;
    this.connected = false;
  }
}
