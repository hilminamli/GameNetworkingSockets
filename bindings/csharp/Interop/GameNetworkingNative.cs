using System;
using System.Runtime.InteropServices;

namespace GameNetworkingSockets
{
    /// <summary>P/Invoke declarations and raw struct field accessors for the GameNetworkingSockets native library.</summary>
    internal static class Native
    {
        // iOS links the transport statically into the player (no dynamic library to
        // load), so P/Invoke must bind against the executable's own symbols via the
        // special "__Internal" name. Every other platform loads the shared library
        // by its file name.
#if UNITY_IOS && !UNITY_EDITOR
        private const string Lib = "__Internal";
#else
        private const string Lib = "GameNetworkingSockets";
#endif

        // ── Init / Kill ──────────────────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool GameNetworkingSockets_Init(
            IntPtr pIdentity,       // const SteamNetworkingIdentity* — pass IntPtr.Zero for anonymous
            byte[] errMsg);         // SteamNetworkingErrMsg = char[1024]

        // Same native entry point, but with an explicit local identity. P2P peers must
        // have distinct identities — the identity is how custom-signaling peers address
        // each other (we use generic strings; Steam is never involved).
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GameNetworkingSockets_Init")]
        internal static extern bool GameNetworkingSockets_InitWithIdentity(
            ref SteamNetworkingIdentity pIdentity,
            byte[] errMsg);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void GameNetworkingSockets_Kill();

        // ── Interface accessor ────────────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SteamAPI_SteamNetworkingSockets_v009();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SteamAPI_SteamNetworkingUtils_v003();

        // ── ISteamNetworkingSockets ───────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(
            IntPtr self,
            ref SteamNetworkingIPAddr localAddress,
            int nOptions,
            IntPtr pOptions);   // SteamNetworkingConfigValue_t* — pass IntPtr.Zero

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress(
            IntPtr self,
            ref SteamNetworkingIPAddr address,
            int nOptions,
            IntPtr pOptions);

        // ── P2P (ICE / custom signaling) ─────────────────────────────────────────
        // These require the library to be built with ENABLE_ICE + USE_STEAMWEBRTC.
        // ConnectP2P / CreateListenSocketP2P use GNS's built-in signaling (SDR on
        // Steam; not available in the open fork). ConnectP2PCustomSignaling drives
        // the connection through a caller-supplied signaling channel — this is the
        // one we bridge to our own Nexus lobby.

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P(
            IntPtr self,
            int nLocalVirtualPort,
            int nOptions,
            IntPtr pOptions);   // SteamNetworkingConfigValue_t* — pass IntPtr.Zero

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint SteamAPI_ISteamNetworkingSockets_ConnectP2P(
            IntPtr self,
            ref SteamNetworkingIdentity identityRemote,
            int nRemoteVirtualPort,
            int nOptions,
            IntPtr pOptions);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint SteamAPI_ISteamNetworkingSockets_ConnectP2PCustomSignaling(
            IntPtr self,
            IntPtr pSignaling,          // ISteamNetworkingConnectionSignaling* (from CreateCustomSignaling)
            ref SteamNetworkingIdentity peerIdentity,
            int nRemoteVirtualPort,
            int nOptions,
            IntPtr pOptions);

        /// <summary>
        /// Builds a native ISteamNetworkingConnectionSignaling from plain C callbacks, so the
        /// signaling bridge (→ Nexus lobby) lives entirely in managed code. Keep the delegates
        /// alive (store them in a field) for the lifetime of the returned object — the GC must
        /// not collect them while GNS holds the function pointers.
        /// </summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SteamAPI_ISteamNetworkingSockets_CreateCustomSignaling(
            IntPtr ctx,
            FnCustomSignalingSendSignal fnSendSignal,
            FnCustomSignalingRelease fnRelease);   // null allowed if no cleanup needed

        /// <summary>
        /// Feed a rendezvous blob received over our signaling channel into GNS. For blobs that
        /// announce a new incoming connection, fnOnConnectRequest is invoked to obtain a signaling
        /// object for the reply direction.
        /// </summary>
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingSockets_ReceivedP2PCustomSignal2(
            IntPtr self,
            IntPtr pMsg,
            int cbMsg,
            IntPtr ctx,
            FnCustomSignalingOnConnectRequest fnOnConnectRequest,
            FnCustomSignalingSendRejectionSignal fnSendRejectionSignal);   // null allowed

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SteamAPI_ISteamNetworkingSockets_AcceptConnection(
            IntPtr self,
            uint hConn);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingSockets_CloseConnection(
            IntPtr self,
            uint hPeer,
            int nReason,
            [MarshalAs(UnmanagedType.LPStr)] string pszDebug,
            [MarshalAs(UnmanagedType.I1)] bool bEnableLinger);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingSockets_CloseListenSocket(
            IntPtr self,
            uint hSocket);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(
            IntPtr self,
            uint hConn,
            IntPtr pData,
            uint cbData,
            int nSendFlags,
            out long pOutMessageNumber);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_ISteamNetworkingSockets_SendMessages(
            IntPtr self,
            int nMessages,
            IntPtr[] ppMessages,
            IntPtr pOutMessageNumberOrResult); // IntPtr.Zero = ignore per-message results

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(
            IntPtr self,
            uint hConn,
            [Out] IntPtr[] ppOutMessages,
            int nMaxMessages);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SteamAPI_ISteamNetworkingSockets_FlushMessagesOnConnection(
            IntPtr self,
            uint hConn);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(
            IntPtr self,
            uint hConn,
            IntPtr pInfo);  // SteamNetConnectionInfo_t* — we read manually

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus(
            IntPtr self,
            uint hConn,
            IntPtr pStatus,  // SteamNetConnectionRealTimeStatus_t* (120 bytes)
            int nLanes,
            IntPtr pLanes);  // pass IntPtr.Zero

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint SteamAPI_ISteamNetworkingSockets_CreatePollGroup(
            IntPtr self);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingSockets_DestroyPollGroup(
            IntPtr self,
            uint hPollGroup);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingSockets_SetConnectionPollGroup(
            IntPtr self,
            uint hConn,
            uint hPollGroup);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnPollGroup(
            IntPtr self,
            uint hPollGroup,
            [Out] IntPtr[] ppOutMessages,
            int nMaxMessages);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_ISteamNetworkingSockets_RunCallbacks(
            IntPtr self);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingSockets_SetConnectionUserData(
            IntPtr self,
            uint hPeer,
            long nUserData);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long SteamAPI_ISteamNetworkingSockets_GetConnectionUserData(
            IntPtr self,
            uint hPeer);

        // ── ISteamNetworkingUtils ─────────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_ISteamNetworkingUtils_SetDebugOutputFunction(
            IntPtr self,
            int eDetailLevel,
            FnDebugOutput pfnFunc);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged(
            IntPtr self,
            FnConnectionStatusChanged fnCallback);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SteamAPI_ISteamNetworkingUtils_AllocateMessage(
            IntPtr self,
            int cbAllocateBuffer);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueInt32(
            IntPtr self,
            int eValue,
            int val);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueString(
            IntPtr self,
            int eValue,
            [MarshalAs(UnmanagedType.LPStr)] string val);

        // ── SteamNetworkingIPAddr flat helpers ────────────────────────────────────

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_SteamNetworkingIPAddr_Clear(
            ref SteamNetworkingIPAddr self);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_SteamNetworkingIPAddr_SetIPv4(
            ref SteamNetworkingIPAddr self,
            uint nIP,
            ushort nPort);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_SteamNetworkingIPAddr_ParseString(
            ref SteamNetworkingIPAddr self,
            [MarshalAs(UnmanagedType.LPStr)] string pszStr);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_SteamNetworkingIPAddr_ToString(
            ref SteamNetworkingIPAddr self,
            byte[] buf,
            UIntPtr cbBuf,
            [MarshalAs(UnmanagedType.I1)] bool bWithPort);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe void SteamAPI_SteamNetworkingIPAddr_ToString(
            ref SteamNetworkingIPAddr self,
            byte* buf,
            UIntPtr cbBuf,
            [MarshalAs(UnmanagedType.I1)] bool bWithPort);

        // ── SteamNetworkingIdentity flat helpers ──────────────────────────────────

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_SteamNetworkingIdentity_Clear(
            ref SteamNetworkingIdentity self);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SteamAPI_SteamNetworkingIdentity_SetGenericString(
            ref SteamNetworkingIdentity self,
            [MarshalAs(UnmanagedType.LPStr)] string pszString);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_SteamNetworkingIdentity_ToString(
            ref SteamNetworkingIdentity self,
            byte[] buf,
            UIntPtr cbBuf);

        // ── SteamNetworkingMessage_t ──────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SteamAPI_SteamNetworkingMessage_t_Release(
            IntPtr self);

        // ── SteamNetworkingMessage_t ──────────────────────────────────────────────
        // Defined outside VALVE_CALLBACK_PACK — default alignment, identical on Windows and Linux x64.
        // m_pData:          offset 0   (IntPtr, 8 bytes)
        // m_cbSize:         offset 8   (int,    4 bytes)
        // m_conn:           offset 12  (uint,   4 bytes)
        // m_nMessageNumber: offset 168 (int64,  8 bytes)
        // m_nFlags:         offset 196 (int,    4 bytes)
        [StructLayout(LayoutKind.Explicit)]
        internal unsafe struct SteamNetworkingMessage_t
        {
            [FieldOffset(0)]   public IntPtr pData;
            [FieldOffset(8)]   public int    cbSize;
            [FieldOffset(12)]  public uint   conn;
            [FieldOffset(168)] public long   messageNumber;
            [FieldOffset(196)] public int    flags;
        }

        // ── SteamNetConnectionStatusChangedCallback_t field offsets ──────────────
        //
        // Windows uses VALVE_CALLBACK_PACK_LARGE (pack 8):
        //   m_hConn at 0, [4 bytes padding], m_info at 8
        // Linux/macOS uses VALVE_CALLBACK_PACK_SMALL (pack 4):
        //   m_hConn at 0, m_info at 4  (no padding)
        //
        // SteamNetConnectionInfo_t layout is identical on both platforms (696 bytes):
        //   m_eState     at +176, m_eEndReason at +180, m_szEndDebug at +184
        //   m_hListenSocket at +144
        //   struct size 696 → m_eOldState follows immediately after m_info
        //
        // m_hConn is always at offset 0 (first field, no padding before it).

        // Offset of m_info within SteamNetConnectionStatusChangedCallback_t.
        // pack(8) on Windows adds 4 bytes of padding after m_hConn (uint32) to align
        // SteamNetConnectionInfo_t to 8 bytes (it contains int64 fields).
        private static readonly int _cbInfoOffset =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 8 : 4;

        // ── SteamNetConnectionInfo_t field offsets ────────────────────────────────
        // m_hListenSocket: offset 144 (uint, 4 bytes) — same on all platforms
        internal static unsafe uint GetConnectionListenSocket(IntPtr iface, uint hConn)
        {
            byte* buf = stackalloc byte[700]; // 696 bytes + 4 padding to avoid overread
            _ = SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(iface, hConn, (IntPtr)buf);
            return *(uint*)(buf + 144);
        }

        internal static uint            Cb_hConn(IntPtr p)     => (uint)Marshal.ReadInt32(p, 0);
        internal static ConnectionState Cb_eState(IntPtr p)    => (ConnectionState)Marshal.ReadInt32(p, _cbInfoOffset + 176);
        internal static int             Cb_endReason(IntPtr p) => Marshal.ReadInt32(p, _cbInfoOffset + 180);
        internal static ConnectionState Cb_eOldState(IntPtr p) => (ConnectionState)Marshal.ReadInt32(p, _cbInfoOffset + 696);
        internal static string          Cb_endDebug(IntPtr p)  => PtrToStringUtf8(p + _cbInfoOffset + 184, 128);

        // GNS emits UTF-8 strings. m_szEndDebug is a fixed 128-byte buffer (k_cchSteamNetworkingMaxConnectionCloseReason).
        internal static unsafe string PtrToStringUtf8(IntPtr ptr, int maxLen)
        {
            if (ptr == IntPtr.Zero) return "";
            var span = new ReadOnlySpan<byte>((void*)ptr, maxLen);
            int end = span.IndexOf((byte)0);
            return System.Text.Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
        }
    }
}
