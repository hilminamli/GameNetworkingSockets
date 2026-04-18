namespace GameNetworkingSockets
{
    /// <summary>Compile-time constants for native interop buffer and struct sizes.</summary>
    internal static class NetworkingConstants
    {
        /// <summary>Max messages processed per ReceiveMessages call. Callers should size their message buffers to match.</summary>
        internal const int MessageBufferSize        = 64;
        /// <summary>Size of SteamNetConnectionRealTimeStatus_t in bytes. Identical on Windows and Linux x64 (no int64 alignment gap under either pack rule).</summary>
        internal const int RealTimeStatusStructSize = 120;
        /// <summary>Size of the byte buffer passed to SteamAPI_SteamNetworkingIPAddr_ToString for the resulting string.</summary>
        internal const int IPAddrStringBufferSize   = 64;
    }
}
