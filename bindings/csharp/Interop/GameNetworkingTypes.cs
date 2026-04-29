using System;
using System.Runtime.InteropServices;

namespace GameNetworkingSockets
{
    /// <summary>Operation result codes returned by GNS send/accept calls. Note: OK = 1, not 0.</summary>
    public enum EResult : int
    {
        OK                  = 1,
        Fail                = 2,
        NoConnection        = 3,
        InvalidParam        = 8,
        Ignored             = 89,
    }

    public enum ConnectionState : int
    {
        None                    = 0,
        Connecting              = 1,
        FindingRoute            = 2,
        Connected               = 3,
        ClosedByPeer            = 4,
        ProblemDetectedLocally  = 5,
    }

    public enum NetworkingAvailability : int
    {
        CannotTry   = -102,
        Failed      = -101,
        Previously  = -100,
        Retrying    = -10,
        NeverTried  = 1,
        Waiting     = 2,
        Attempting  = 3,
        Current     = 100,
        Unknown     = 0,
    }

    /// <summary>Message send flags. Reliable guarantees delivery and order; Unreliable is fire-and-forget. NoNagle forces immediate send without buffering.</summary>
    public enum SendType : int
    {
        Unreliable          = 0,
        UnreliableNoNagle   = 1,
        UnreliableNoDelay   = 5,
        Reliable            = 8,
        ReliableNoNagle     = 9,
    }

    /// <summary>
    /// Subset of <c>ESteamNetworkingConfigValue</c> covering the int32 settings most callers want to tune.
    /// All values can be set globally via <see cref="NetworkingLibrary.SetGlobalConfig"/>; settings tagged
    /// "[connection int32]" in the GNS headers also propagate as defaults to every connection created
    /// after the call. Defaults are GNS native values — none are overridden by this binding.
    /// </summary>
    public enum NetworkingConfigValue : int
    {
        // ── Throughput / flow control ─────────────────────────────────────────
        /// <summary>Upper limit of buffered pending bytes to be sent. Hitting it makes <c>SendMessage</c> return <see cref="EResult.Fail"/>. Default: 524288 (512 KB).</summary>
        SendBufferSize           = 9,
        /// <summary>Minimum send rate clamp in bytes/sec. Default: 262144 (256 KB/s).</summary>
        SendRateMin              = 10,
        /// <summary>Maximum send rate clamp in bytes/sec. Default: 262144 (256 KB/s). Set equal to <see cref="SendRateMin"/> to pin a constant rate.</summary>
        SendRateMax              = 11,
        /// <summary>Nagle coalescing delay in microseconds. Set to 0 to disable Nagle (each small message ships immediately). Default: 5000 (5 ms).</summary>
        NagleTime                = 12,
        /// <summary>Upper limit on buffered inbound bytes. Exceeded packets are dropped. Default: 1048576 (1 MB).</summary>
        RecvBufferSize           = 47,
        /// <summary>Upper limit on buffered inbound message count. Exceeded packets are dropped. Default: 1000.</summary>
        RecvBufferMessages       = 48,
        /// <summary>Largest single message we are willing to receive. Sender exceeding this gets disconnected. Default: 524288 (512 KB).</summary>
        RecvMaxMessageSize       = 49,
        /// <summary>Max message segments accepted in a single UDP packet. Tune up if Nagle is disabled and the sender ships many small messages.</summary>
        RecvMaxSegmentsPerPacket = 50,
        /// <summary>Outbound UDP packet payload size in bytes. Lower this for restrictive networks. Default: 1300.</summary>
        MTU_PacketSize           = 32,

        // ── Timeouts ──────────────────────────────────────────────────────────
        /// <summary>Connect-phase timeout in milliseconds. Default: 10000.</summary>
        TimeoutInitial           = 24,
        /// <summary>Idle-disconnect timeout once connected, in milliseconds. Default: 10000.</summary>
        TimeoutConnected         = 25,

        // ── Authentication (IP transport) ─────────────────────────────────────
        /// <summary>Allow IP connections without strong auth. 0 = strict, 1 = warn-only, 2 = silent. Dev/debug only.</summary>
        IP_AllowWithoutAuth      = 23,
        /// <summary>Same as <see cref="IP_AllowWithoutAuth"/> but only for localhost peers.</summary>
        IPLocalHost_AllowWithoutAuth = 52,
    }

    public enum DebugOutputType : int
    {
        None        = 0,
        Bug         = 1,
        Error       = 2,
        Important   = 3,
        Warning     = 4,
        Msg         = 5,
        Verbose     = 6,
        Debug       = 7,
        Everything  = 8,
    }

    // SteamNetworkingIPAddr — pragma pack(1), 18 bytes total
    // Layout: 16 bytes IPv6 union + 2 bytes port
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 18)]
    public struct SteamNetworkingIPAddr
    {
        [FieldOffset(0)]  public ulong ipv6Lo;
        [FieldOffset(8)]  public ulong ipv6Hi;
        [FieldOffset(16)] public ushort port;

        // IPv4-mapped address helpers
        // IPv4 mapped: ::ffff:aa.bb.cc.dd
        // ipv6Lo = 0x0000_0000_0000_0000
        // ipv6Hi = 0xddcc_bbaa_ffff_0000 (little-endian)
        /// <summary>Returns true if this is an IPv4-mapped IPv6 address (::ffff:x.x.x.x).</summary>
        public readonly bool IsIPv4 =>
            ipv6Lo == 0UL && (ipv6Hi & 0x0000_FFFF_FFFF_FFFFUL) == 0x0000_FFFF_0000_0000UL;

        /// <summary>Extracts the IPv4 address from the upper 32 bits of ipv6Hi (little-endian).</summary>
        public readonly uint IPv4 => (uint)((ipv6Hi >> 32) & 0xFFFFFFFF);
    }

    // SteamNetworkingIdentity — pragma pack(1), 136 bytes total
    // Layout: 4 (eType) + 4 (cbSize) + 128 (union)
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 136)]
    public struct SteamNetworkingIdentity
    {
        [FieldOffset(0)] public int eType;
        [FieldOffset(4)] public int cbSize;

        // union at offset 8, 128 bytes (m_reserved[32])
        [FieldOffset(8)]   public ulong steamID64;
        [FieldOffset(8)]   private readonly ulong _u0;
        [FieldOffset(16)]  private readonly ulong _u1;
        [FieldOffset(24)]  private readonly ulong _u2;
        [FieldOffset(32)]  private readonly ulong _u3;
        [FieldOffset(40)]  private readonly ulong _u4;
        [FieldOffset(48)]  private readonly ulong _u5;
        [FieldOffset(56)]  private readonly ulong _u6;
        [FieldOffset(64)]  private readonly ulong _u7;
        [FieldOffset(72)]  private readonly ulong _u8;
        [FieldOffset(80)]  private readonly ulong _u9;
        [FieldOffset(88)]  private readonly ulong _u10;
        [FieldOffset(96)]  private readonly ulong _u11;
        [FieldOffset(104)] private readonly ulong _u12;
        [FieldOffset(112)] private readonly ulong _u13;
        [FieldOffset(120)] private readonly ulong _u14;
        [FieldOffset(128)] private readonly ulong _u15;
    }

    /// <summary>
    /// Callback invoked by NetworkingSockets.ReceiveMessages for each received message.
    /// The span points directly at native memory and is only valid for the duration of the callback —
    /// do not capture it, await across it, or hand it to another thread. Copy out anything you need.
    /// </summary>
    public delegate void MessageReceivedCallback(uint hConn, ReadOnlySpan<byte> data);

    // Connection state change event data
    public struct ConnectionStatusInfo
    {
        public uint            connection;
        public ConnectionState state;
        public ConnectionState oldState;
        /// <summary>Application-defined reason code sent by the closer, or a GNS internal code if closed by the library.</summary>
        public int             endReason;
        /// <summary>Human-readable disconnect reason. Only populated on ClosedByPeer and ProblemDetectedLocally states; null otherwise.</summary>
        public string          endDebug;
    }

    /// <summary>Full real-time connection statistics from SteamNetConnectionRealTimeStatus_t.</summary>
    public struct ConnectionStats
    {
        /// <summary>Current RTT ping in milliseconds.</summary>
        public int   PingMs;
        /// <summary>Packet loss ratio as seen locally (0..1). 0 = no loss.</summary>
        public float PacketLossLocal;
        /// <summary>Packet loss ratio as reported by the remote peer (0..1).</summary>
        public float PacketLossRemote;
        /// <summary>Outgoing packets per second.</summary>
        public float OutPacketsPerSec;
        /// <summary>Outgoing bytes per second.</summary>
        public float OutBytesPerSec;
        /// <summary>Incoming packets per second.</summary>
        public float InPacketsPerSec;
        /// <summary>Incoming bytes per second.</summary>
        public float InBytesPerSec;
        /// <summary>Estimated channel send capacity in bytes/sec. May exceed OutBytesPerSec if you are under the limit.</summary>
        public int   SendRateBytesPerSecond;
        /// <summary>Bytes queued to send (unreliable), including Nagle-delayed data.</summary>
        public int   PendingUnreliable;
        /// <summary>Bytes queued to send (reliable), including data pending retransmit.</summary>
        public int   PendingReliable;
        /// <summary>Reliable bytes sent but not yet acknowledged — may need retransmit.</summary>
        public int   SentUnackedReliable;
        /// <summary>Estimated time (microseconds) a new message would wait in the send queue before hitting the wire.</summary>
        public long  QueueTimeMicroseconds;
    }

    // Delegate types for native callbacks
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FnConnectionStatusChanged(IntPtr pInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FnDebugOutput(int nType, IntPtr pszMsg);
}
