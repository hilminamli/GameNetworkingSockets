using System;

namespace GameNetworkingSockets.Transport
{
    /// <summary>
    /// Side channel that carries P2P rendezvous blobs between peers before (and while) the GNS
    /// connection exists — typically the lobby-server connection. Implementations move opaque
    /// bytes only; the blobs are GNS-internal and never interpreted here.
    /// </summary>
    public interface ISignalingChannel
    {
        /// <summary>
        /// Delivers a rendezvous blob to the peer identified by <paramref name="toIdentity"/>
        /// (a GNS generic identity string, e.g. a lobby-assigned player id). May be called from
        /// GNS's internal service thread — implementations must be thread-safe.
        /// </summary>
        void Send(string toIdentity, byte[] blob);

        /// <summary>
        /// Fired when a rendezvous blob arrives from a remote peer. May fire on any thread;
        /// the transport queues the blob and processes it on its own Tick.
        /// </summary>
        event Action<byte[]> BlobReceived;
    }
}
