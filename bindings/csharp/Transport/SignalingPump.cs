using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace GameNetworkingSockets.Transport
{
    /// <summary>
    /// Glue between an <see cref="ISignalingChannel"/> and GNS custom signaling, shared by the
    /// P2P client and server transports. Outbound: GNS's send callback (possibly on its service
    /// thread) copies the blob and forwards it over the channel, addressed by the remote peer's
    /// identity. Inbound: channel blobs land in a queue and are fed to GNS from
    /// <see cref="Drain"/> on the transport's Tick thread — so connect-request callbacks
    /// (adoption, accept) never race the pump loop.
    /// </summary>
    internal sealed class SignalingPump
    {
        private readonly ISignalingChannel _channel;
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();

        // Kept as fields so the delegates P2PSignaling roots are the same instances across calls.
        private readonly FnCustomSignalingSendSignal _sendSignal;
        private readonly FnCustomSignalingOnConnectRequest _onConnectRequest;

        internal SignalingPump(ISignalingChannel channel, FnCustomSignalingOnConnectRequest onConnectRequest)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _onConnectRequest = onConnectRequest ?? throw new ArgumentNullException(nameof(onConnectRequest));
            _sendSignal = SendSignal;
            channel.BlobReceived += blob => _inbox.Enqueue(blob);
        }

        /// <summary>
        /// Creates a fresh native signaling object bound to this pump's send path. GNS takes
        /// ownership of the returned object per connection, so callers need a new one for every
        /// outbound connect and every accepted connect request.
        /// </summary>
        internal IntPtr CreateSignalingObject()
            => P2PSignaling.CreateSignalingObject(_sendSignal);

        /// <summary>Feeds queued inbound blobs into GNS. Call once per transport Tick.</summary>
        internal void Drain()
        {
            while (_inbox.TryDequeue(out var blob))
                _ = P2PSignaling.ReceivedSignal(blob, _onConnectRequest);
        }

        private bool SendSignal(IntPtr ctx, uint hConn, IntPtr pInfo, IntPtr pMsg, int cbMsg)
        {
            var blob = new byte[cbMsg];
            Marshal.Copy(pMsg, blob, 0, cbMsg);
            try
            {
                _channel.Send(P2PSignaling.RemoteIdentityOf(pInfo), blob);
                return true;
            }
            catch
            {
                // Returning false tells GNS the signal could not be delivered; it will retry
                // or fail the connection — never let a channel exception cross into native.
                return false;
            }
        }
    }
}
