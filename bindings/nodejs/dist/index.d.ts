import { EventEmitter } from 'node:events';
/** GNS send flags (k_nSteamNetworkingSend_*). */
export declare const SendType: {
    readonly Unreliable: 0;
    readonly NoNagle: 1;
    readonly Reliable: 8;
    readonly ReliableNoNagle: 9;
};
export type SendType = (typeof SendType)[keyof typeof SendType];
/** Initializes the GNS library. Idempotent. Called automatically by the first peer. */
export declare function init(): void;
/** Shuts down the GNS library. Call after destroying all peers. */
export declare function shutdown(): void;
/** Sets how often (ms) the library polls for messages/callbacks. Default 16ms (~60Hz). */
export declare function setTickInterval(ms: number): void;
declare abstract class PeerBase extends EventEmitter {
    protected peerId: number;
    protected destroyed: boolean;
    protected register(): void;
    /** @internal Drains pending messages and emits 'message'. Called by the central tick. */
    _drain(): void;
    /** Real-time ping (ms) and packet loss (0..1) for a connection, or null. */
    getStatus(conn: number): {
        ping: number;
        packetLoss: number;
    } | null;
    destroy(): void;
}
/**
 * Listens for incoming connections. Auto-accepts and pools them.
 * Events:
 *   'connect'    (conn:number)
 *   'disconnect' (conn:number, endReason:number, endDebug:string)
 *   'message'    (conn:number, data:Buffer, flags:number)
 */
export declare class GnsServer extends PeerBase {
    readonly clients: Set<number>;
    constructor(port: number);
    /** Sends to one client. */
    send(conn: number, data: Buffer, sendType?: SendType): number;
    /** Sends to every connected client. */
    broadcast(data: Buffer, sendType?: SendType): void;
    /** Disconnects a client. */
    kick(conn: number, reason?: number, debug?: string): void;
}
/**
 * Connects to a remote server. In a multi-server mesh, each node runs a GnsServer
 * and opens a GnsClient per peer it dials out to.
 * Events:
 *   'connect'    (conn:number)
 *   'disconnect' (conn:number, endReason:number, endDebug:string)
 *   'message'    (conn:number, data:Buffer, flags:number)
 */
export declare class GnsClient extends PeerBase {
    conn: number;
    connected: boolean;
    constructor();
    /** Initiates a connection to "ip:port". Returns the connection handle (0 on failure). */
    connect(target: string): number;
    /** Sends to the active server connection. */
    send(data: Buffer, sendType?: SendType): number;
    /** Closes the active connection. */
    disconnect(reason?: number, debug?: string): void;
}
export {};
