namespace GameNetworkingSockets
{
    /// <summary>Compile-time constants for native interop buffer and struct sizes.</summary>
    internal static class NetworkingConstants
    {
        /// <summary>
        /// Default max messages drained from a single client connection per <c>ReceiveMessages</c> call.
        /// Clients typically own one connection with bounded inbound traffic, so a smaller buffer is enough.
        /// </summary>
        internal const int DefaultClientMessageBufferSize = 64;
        /// <summary>
        /// Default max messages drained from a server poll group per <c>ReceiveMessages</c> call.
        /// Servers receive from every connected client in a single call, so a larger buffer reduces
        /// the number of native calls per tick under load. Tune up for high client counts or burst traffic.
        /// </summary>
        internal const int DefaultServerMessageBufferSize = 256;
        /// <summary>Size of SteamNetConnectionRealTimeStatus_t in bytes. Identical on Windows and Linux x64 (no int64 alignment gap under either pack rule).</summary>
        internal const int RealTimeStatusStructSize = 120;
        /// <summary>Size of the byte buffer passed to SteamAPI_SteamNetworkingIPAddr_ToString for the resulting string.</summary>
        internal const int IPAddrStringBufferSize   = 64;
    }
}
