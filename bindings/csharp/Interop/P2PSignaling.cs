using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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

        // SteamNetworkingIdentity layout: m_eType at +0, m_cbSize at +4, union at +8.
        // k_ESteamNetworkingIdentityType_GenericString = 2; m_szGenericString is char[32].
        private const int GenericStringType = 2;
        private const int IdentityUnionOffset = 8;
        private const int GenericStringMaxBytes = 32;

        /// <summary>
        /// Reads the remote peer's identity from a <c>SteamNetConnectionInfo_t*</c> — the
        /// <c>pInfo</c> handed to signaling callbacks (its first field is
        /// <c>m_identityRemote</c>). Returns the generic string for generic-string identities,
        /// or GNS's "type:value" rendering for anything else.
        /// </summary>
        public static string RemoteIdentityOf(IntPtr pConnectionInfo)
        {
            if (pConnectionInfo == IntPtr.Zero) return "";

            int eType = Marshal.ReadInt32(pConnectionInfo, 0);
            if (eType == GenericStringType)
                return Native.PtrToStringUtf8(pConnectionInfo + IdentityUnionOffset, GenericStringMaxBytes);

            var identity = Marshal.PtrToStructure<SteamNetworkingIdentity>(pConnectionInfo);
            return IdentityToString(ref identity);
        }

        /// <summary>Renders an identity via GNS's canonical "type:value" format (e.g. "str:player-7").</summary>
        public static string IdentityToString(ref SteamNetworkingIdentity identity)
        {
            var buf = new byte[128];
            Native.SteamAPI_SteamNetworkingIdentity_ToString(ref identity, buf, (UIntPtr)buf.Length);
            int end = Array.IndexOf(buf, (byte)0);
            return System.Text.Encoding.UTF8.GetString(buf, 0, end < 0 ? buf.Length : end);
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
            => CreateSignalingObject(IntPtr.Zero, sendSignal, release);

        /// <summary>
        /// As <see cref="CreateSignalingObject(FnCustomSignalingSendSignal, FnCustomSignalingRelease)"/>,
        /// but passes <paramref name="ctx"/> back to the callbacks — required for IL2CPP AOT, where
        /// the callbacks must be static thunks that recover their instance from this context pointer.
        /// </summary>
        public static IntPtr CreateSignalingObject(IntPtr ctx, FnCustomSignalingSendSignal sendSignal, FnCustomSignalingRelease release = null)
        {
            if (sendSignal == null) throw new ArgumentNullException(nameof(sendSignal));
            Root(sendSignal);
            Root(release);
            return Native.SteamAPI_ISteamNetworkingSockets_CreateCustomSignaling(ctx, sendSignal, release);
        }

        /// <summary>
        /// Feeds a rendezvous blob received over the lobby into GNS. Call from your receive loop.
        /// For blobs that announce a new incoming connection, <paramref name="onConnectRequest"/>
        /// runs inline and must return a signaling object for the reply direction.
        /// </summary>
        public static bool ReceivedSignal(ReadOnlySpan<byte> blob,
            FnCustomSignalingOnConnectRequest onConnectRequest,
            FnCustomSignalingSendRejectionSignal sendRejection = null)
            => ReceivedSignal(blob, IntPtr.Zero, onConnectRequest, sendRejection);

        /// <summary>
        /// As <see cref="ReceivedSignal(ReadOnlySpan{byte}, FnCustomSignalingOnConnectRequest, FnCustomSignalingSendRejectionSignal)"/>,
        /// but passes <paramref name="ctx"/> back to the callbacks — required for IL2CPP AOT static thunks.
        /// </summary>
        public static unsafe bool ReceivedSignal(ReadOnlySpan<byte> blob, IntPtr ctx,
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
                    iface, (IntPtr)p, blob.Length, ctx, onConnectRequest, sendRejection);
            }
        }
    }
}
