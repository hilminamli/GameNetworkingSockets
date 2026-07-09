using System;
using System.Collections.Generic;

namespace GameNetworkingSockets
{
    /// <summary>
    /// Managed entry points for GNS custom signaling — the mechanism that lets P2P/ICE
    /// rendezvous blobs travel over OUR signaling channel (the lobby server) instead of Steam.
    ///
    /// Outbound: <see cref="CreateSignalingObject"/> wraps managed callbacks into a native
    /// <c>ISteamNetworkingConnectionSignaling*</c>. GNS invokes the send callback whenever it
    /// has a rendezvous blob for the peer — forward that blob over the lobby. Pass the object
    /// to <see cref="NetworkingClient.ConnectP2PCustomSignaling"/>, or return one from an
    /// OnConnectRequest callback to answer an incoming connection.
    ///
    /// Inbound: when the lobby delivers a blob from a remote peer, feed it to
    /// <see cref="ReceivedSignal"/>. If the blob announces a NEW incoming connection, the
    /// onConnectRequest callback is invoked and must return a signaling object for the reply
    /// direction (or <see cref="IntPtr.Zero"/> to ignore the request).
    ///
    /// Lifetime: delegates are rooted internally so the GC never collects them while native
    /// code holds their function pointers. Signaling objects are owned by GNS after hand-off;
    /// the optional release callback fires when GNS is done with one.
    /// </summary>
    public static class P2PSignaling
    {
        // Native code holds these function pointers indefinitely — root them for process lifetime.
        private static readonly List<Delegate> _rooted = new List<Delegate>();
        private static readonly object _lock = new object();

        private static void Root(Delegate d)
        {
            if (d == null) return;
            lock (_lock) _rooted.Add(d);
        }

        /// <summary>Builds a <see cref="SteamNetworkingIdentity"/> from a generic string (max 31 chars). Not a SteamID — just an opaque peer label.</summary>
        public static SteamNetworkingIdentity MakeGenericIdentity(string genericString)
        {
            var identity = new SteamNetworkingIdentity();
            Native.SteamAPI_SteamNetworkingIdentity_Clear(ref identity);
            if (!Native.SteamAPI_SteamNetworkingIdentity_SetGenericString(ref identity, genericString))
                throw new ArgumentException($"Invalid generic identity string: \"{genericString}\" (max 31 chars).", nameof(genericString));
            return identity;
        }

        /// <summary>
        /// Creates a native signaling object from managed callbacks. GNS calls
        /// <paramref name="sendSignal"/> (possibly from its internal service thread) whenever a
        /// rendezvous blob must reach the peer — copy the blob out and forward it over the lobby.
        /// </summary>
        /// <returns>Native <c>ISteamNetworkingConnectionSignaling*</c>, or <see cref="IntPtr.Zero"/> on failure.</returns>
        public static IntPtr CreateSignalingObject(FnCustomSignalingSendSignal sendSignal, FnCustomSignalingRelease release = null)
        {
            if (sendSignal == null) throw new ArgumentNullException(nameof(sendSignal));
            Root(sendSignal);
            Root(release);
            return Native.SteamAPI_ISteamNetworkingSockets_CreateCustomSignaling(IntPtr.Zero, sendSignal, release);
        }

        /// <summary>
        /// Feeds a rendezvous blob received over the lobby into GNS. Call from your receive loop.
        /// For blobs that announce a new incoming connection, <paramref name="onConnectRequest"/>
        /// runs inline and must return a signaling object for the reply direction.
        /// </summary>
        public static unsafe bool ReceivedSignal(ReadOnlySpan<byte> blob,
            FnCustomSignalingOnConnectRequest onConnectRequest,
            FnCustomSignalingSendRejectionSignal sendRejection = null)
        {
            if (onConnectRequest == null) throw new ArgumentNullException(nameof(onConnectRequest));
            if (!NetworkingLibrary.IsInitialized)
                throw new InvalidOperationException("Call NetworkingLibrary.Initialize() first.");

            Root(onConnectRequest);
            Root(sendRejection);

            IntPtr iface = Native.SteamAPI_SteamNetworkingSockets_v009();
            fixed (byte* p = blob)
            {
                return Native.SteamAPI_ISteamNetworkingSockets_ReceivedP2PCustomSignal2(
                    iface, (IntPtr)p, blob.Length, IntPtr.Zero, onConnectRequest, sendRejection);
            }
        }
    }
}
