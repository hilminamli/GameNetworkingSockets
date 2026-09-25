"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.GnsClient = exports.GnsServer = exports.SendType = void 0;
exports.init = init;
exports.shutdown = shutdown;
exports.setTickInterval = setTickInterval;
const node_events_1 = require("node:events");
const path = __importStar(require("node:path"));
// Load the N-API addon. node-gyp-build picks the right artifact automatically:
//   1. a prebuilt binary under prebuilds/<platform>-<arch>/ (shipped in the package), or
//   2. a locally compiled build/Release/gns.node (dev / `npm run build:native`).
// The package root is one level up from dist/ (where this file lives at runtime).
const pkgRoot = path.join(__dirname, '..');
// eslint-disable-next-line @typescript-eslint/no-var-requires
const native = require('node-gyp-build')(pkgRoot);
/** GNS send flags (k_nSteamNetworkingSend_*). */
exports.SendType = {
    Unreliable: 0,
    NoNagle: 1,
    Reliable: 8,
    ReliableNoNagle: 9,
};
// ── Library lifecycle ───────────────────────────────────────────────────────────
let initialized = false;
/** Initializes the GNS library. Idempotent. Called automatically by the first peer. */
function init() {
    if (initialized)
        return;
    if (!native.init())
        throw new Error('GameNetworkingSockets_Init failed');
    initialized = true;
}
/** Shuts down the GNS library. Call after destroying all peers. */
function shutdown() {
    stopTick();
    native.kill();
    initialized = false;
}
// ── Central tick: one timer drives RunCallbacks + per-peer poll for all peers ────
const activePeers = new Set();
let tickTimer = null;
let tickIntervalMs = 16; // ~60Hz default
function startTick() {
    if (tickTimer)
        return;
    tickTimer = setInterval(() => {
        native.runCallbacks(); // fires status callbacks synchronously into each peer's onStatus
        for (const peer of activePeers)
            peer._drain();
    }, tickIntervalMs);
    // Don't keep the event loop alive solely for the tick.
    tickTimer.unref?.();
}
function stopTick() {
    if (tickTimer) {
        clearInterval(tickTimer);
        tickTimer = null;
    }
}
/** Sets how often (ms) the library polls for messages/callbacks. Default 16ms (~60Hz). */
function setTickInterval(ms) {
    tickIntervalMs = Math.max(1, ms);
    if (tickTimer) {
        stopTick();
        startTick();
    }
}
// ── Shared peer base ────────────────────────────────────────────────────────────
class PeerBase extends node_events_1.EventEmitter {
    peerId = 0;
    destroyed = false;
    register() {
        activePeers.add(this);
        startTick();
    }
    /** @internal Drains pending messages and emits 'message'. Called by the central tick. */
    _drain() {
        if (this.destroyed)
            return;
        const msgs = native.poll(this.peerId);
        for (const m of msgs)
            this.emit('message', m.conn, m.data, m.flags);
    }
    /** Real-time ping (ms) and packet loss (0..1) for a connection, or null. */
    getStatus(conn) {
        return native.getConnectionStatus(conn);
    }
    destroy() {
        if (this.destroyed)
            return;
        this.destroyed = true;
        activePeers.delete(this);
        native.destroyPeer(this.peerId);
        if (activePeers.size === 0)
            stopTick();
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
class GnsServer extends PeerBase {
    clients = new Set();
    constructor(port) {
        super();
        init();
        this.peerId = native.createServer(port, (event, conn, endReason, endDebug) => {
            if (event === 'connect') {
                this.clients.add(conn);
                this.emit('connect', conn);
            }
            else {
                this.clients.delete(conn);
                this.emit('disconnect', conn, endReason, endDebug);
            }
        });
        this.register();
    }
    /** Sends to one client. */
    send(conn, data, sendType = exports.SendType.Reliable) {
        return native.send(conn, data, sendType);
    }
    /** Sends to every connected client. */
    broadcast(data, sendType = exports.SendType.Reliable) {
        for (const conn of this.clients)
            native.send(conn, data, sendType);
    }
    /** Disconnects a client. */
    kick(conn, reason = 0, debug) {
        native.closeConnection(conn, reason, debug);
        this.clients.delete(conn);
    }
}
exports.GnsServer = GnsServer;
// ── Client ──────────────────────────────────────────────────────────────────────
/**
 * Connects to a remote server. In a multi-server mesh, each node runs a GnsServer
 * and opens a GnsClient per peer it dials out to.
 * Events:
 *   'connect'    (conn:number)
 *   'disconnect' (conn:number, endReason:number, endDebug:string)
 *   'message'    (conn:number, data:Buffer, flags:number)
 */
class GnsClient extends PeerBase {
    conn = 0;
    connected = false;
    constructor() {
        super();
        init();
        this.peerId = native.createClient((event, conn, endReason, endDebug) => {
            if (event === 'connect') {
                this.connected = true;
                this.emit('connect', conn);
            }
            else {
                this.connected = false;
                this.conn = 0;
                this.emit('disconnect', conn, endReason, endDebug);
            }
        });
        this.register();
    }
    /** Initiates a connection to "ip:port". Returns the connection handle (0 on failure). */
    connect(target) {
        this.conn = native.connect(this.peerId, target);
        return this.conn;
    }
    /** Sends to the active server connection. */
    send(data, sendType = exports.SendType.Reliable) {
        if (!this.conn)
            throw new Error('Not connected');
        return native.send(this.conn, data, sendType);
    }
    /** Closes the active connection. */
    disconnect(reason = 0, debug) {
        if (this.conn)
            native.closeConnection(this.conn, reason, debug);
        this.conn = 0;
        this.connected = false;
    }
}
exports.GnsClient = GnsClient;
