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
    ///
    /// IL2CPP AOT (iOS) can only marshal STATIC methods to native function pointers, so the two
    /// callbacks GNS holds (send-signal, connect-request) are static thunks. Each native call
    /// carries a <c>ctx</c> pointer — the GCHandle of the owning pump — which the thunk unwraps to
    /// reach this instance. On desktop Mono this path is identical; it just also happens to be
    /// AOT-safe.
    /// </summary>
    internal sealed class SignalingPump : IDisposable
    {
        private readonly ISignalingChannel _channel;
        private readonly ConcurrentQueue<byte[]> _inbox = new ConcurrentQueue<byte[]>();
        private readonly ManagedConnectRequest _onConnectRequest;
        private GCHandle _self;

        // Static thunks GNS holds as function pointers (one instance each, rooted by P2PSignaling).
        private static readonly FnCustomSignalingSendSignal _sendSignalThunk = SendSignalThunk;
        private static readonly FnCustomSignalingOnConnectRequest _onConnectRequestThunk = OnConnectRequestThunk;

        /// <summary>Managed connect-request handler shape (no native marshaling — called from the static thunk).</summary>
        internal delegate IntPtr ManagedConnectRequest(uint hConn, ref SteamNetworkingIdentity identityPeer, int nLocalVirtualPort);

        internal SignalingPump(ISignalingChannel channel, ManagedConnectRequest onConnectRequest)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _onConnectRequest = onConnectRequest ?? throw new ArgumentNullException(nameof(onConnectRequest));
            _self = GCHandle.Alloc(this);
            channel.BlobReceived += blob => _inbox.Enqueue(blob);
        }

        private IntPtr Ctx => GCHandle.ToIntPtr(_self);

        /// <summary>
        /// Creates a fresh native signaling object bound to this pump's send path. GNS takes
        /// ownership of the returned object per connection, so callers need a new one for every
        /// outbound connect and every accepted connect request.
        /// </summary>
        internal IntPtr CreateSignalingObject()
            => P2PSignaling.CreateSignalingObject(Ctx, _sendSignalThunk);

        /// <summary>Feeds queued inbound blobs into GNS. Call once per transport Tick.</summary>
        internal void Drain()
        {
            while (_inbox.TryDequeue(out var blob))
                _ = P2PSignaling.ReceivedSignal(blob, Ctx, _onConnectRequestThunk);
        }

        public void Dispose()
        {
            if (_self.IsAllocated) _self.Free();
        }

        // ── Static thunks (AOT-safe) ──────────────────────────────────────────────

        private static SignalingPump FromCtx(IntPtr ctx)
            => ctx == IntPtr.Zero ? null : GCHandle.FromIntPtr(ctx).Target as SignalingPump;

        [MonoPInvokeCallback(typeof(FnCustomSignalingSendSignal))]
        private static bool SendSignalThunk(IntPtr ctx, uint hConn, IntPtr pInfo, IntPtr pMsg, int cbMsg)
        {
            var pump = FromCtx(ctx);
            if (pump == null) return false;

            var blob = new byte[cbMsg];
            Marshal.Copy(pMsg, blob, 0, cbMsg);
            try
            {
                pump._channel.Send(P2PSignaling.RemoteIdentityOf(pInfo), blob);
                return true;
            }
            catch
            {
                // Returning false tells GNS the signal could not be delivered; it will retry
                // or fail the connection — never let a channel exception cross into native.
                return false;
            }
        }

        [MonoPInvokeCallback(typeof(FnCustomSignalingOnConnectRequest))]
        private static IntPtr OnConnectRequestThunk(IntPtr ctx, uint hConn, ref SteamNetworkingIdentity identityPeer, int nLocalVirtualPort)
        {
            var pump = FromCtx(ctx);
            if (pump == null) return IntPtr.Zero;
            return pump._onConnectRequest(hConn, ref identityPeer, nLocalVirtualPort);
        }
    }
}
