namespace Valve.Sockets
{
    /// <summary>Compile-time constants for native interop buffer and struct sizes.</summary>
    internal static class NetworkingConstants
    {
        internal const int MessageBufferSize        = 64;
        internal const int RealTimeStatusStructSize = 120;
        internal const int IPAddrStringBufferSize   = 64;
    }
}
