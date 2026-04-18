using System;
using System.Runtime.InteropServices;

namespace Valve.Sockets
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
        public bool IsIPv4 =>
            ipv6Lo == 0UL && (ipv6Hi & 0x0000_FFFF_FFFF_FFFFUL) == 0x0000_FFFF_0000_0000UL;

        /// <summary>Extracts the IPv4 address from the upper 32 bits of ipv6Hi (little-endian).</summary>
        public uint IPv4 => (uint)((ipv6Hi >> 32) & 0xFFFFFFFF);
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
        [FieldOffset(8)]   private ulong _u0;
        [FieldOffset(16)]  private ulong _u1;
        [FieldOffset(24)]  private ulong _u2;
        [FieldOffset(32)]  private ulong _u3;
        [FieldOffset(40)]  private ulong _u4;
        [FieldOffset(48)]  private ulong _u5;
        [FieldOffset(56)]  private ulong _u6;
        [FieldOffset(64)]  private ulong _u7;
        [FieldOffset(72)]  private ulong _u8;
        [FieldOffset(80)]  private ulong _u9;
        [FieldOffset(88)]  private ulong _u10;
        [FieldOffset(96)]  private ulong _u11;
        [FieldOffset(104)] private ulong _u12;
        [FieldOffset(112)] private ulong _u13;
        [FieldOffset(120)] private ulong _u14;
        [FieldOffset(128)] private ulong _u15;
    }

    // Received message — populated by NetworkingSockets.ReceiveMessages
    public struct NetworkMessage
    {
        /// <summary>GNS connection handle the message arrived on.</summary>
        public uint   connection;
        /// <summary>Monotonically increasing sequence number assigned by GNS.</summary>
        public long   messageNumber;
        /// <summary>Send flags the sender used (see SendType).</summary>
        public int    flags;
        public byte[] data;
    }

    // Connection state change event data
    public struct ConnectionStatusInfo
    {
        public uint            connection;
        public ConnectionState state;
        public ConnectionState oldState;
        /// <summary>Application-defined reason code sent by the closer, or a GNS internal code if closed by the library.</summary>
        public int             endReason;
        /// <summary>Human-readable debug string describing why the connection ended.</summary>
        public string          endDebug;
    }

    // Delegate types for native callbacks
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FnConnectionStatusChanged(IntPtr pInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FnDebugOutput(int nType, IntPtr pszMsg);
}
