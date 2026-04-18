using System;
using System.Runtime.InteropServices;

namespace Valve.Sockets
{
    /// <summary>P/Invoke declarations and raw struct field accessors for the GameNetworkingSockets native library.</summary>
    internal static class Native
    {
        private const string Lib = "GameNetworkingSockets";

        // ── Init / Kill ──────────────────────────────────────────────────────────

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool GameNetworkingSockets_Init(
            IntPtr pIdentity,       // const SteamNetworkingIdentity* — pass IntPtr.Zero for anonymous
            byte[] errMsg);         // SteamNetworkingErrMsg = char[1024]

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
            [MarshalAs(UnmanagedType.LPStr)] string? pszDebug,
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
            byte[] pData,
            uint cbData,
            int nSendFlags,
            out long pOutMessageNumber);

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

        // ── SteamNetworkingMessage_t field offsets (x64 Windows, no extra pack) ──
        // m_pData:         offset 0   (IntPtr, 8 bytes)
        // m_cbSize:        offset 8   (int,    4 bytes)
        // m_conn:          offset 12  (uint,   4 bytes)
        // m_nFlags:        offset 196 (int,    4 bytes)
        // m_nMessageNumber:offset 168 (int64,  8 bytes)
        internal static IntPtr  Msg_pData(IntPtr msg)          => Marshal.ReadIntPtr(msg, 0);
        internal static int     Msg_cbSize(IntPtr msg)         => Marshal.ReadInt32(msg, 8);
        internal static uint    Msg_conn(IntPtr msg)           => (uint)Marshal.ReadInt32(msg, 12);
        internal static long    Msg_messageNumber(IntPtr msg)  => Marshal.ReadInt64(msg, 168);
        internal static int     Msg_flags(IntPtr msg)          => Marshal.ReadInt32(msg, 196);

        // ── SteamNetConnectionStatusChangedCallback_t field offsets ──────────────
        // (pack 8 on Windows x64)
        // m_hConn:              offset 0   (uint,   4 bytes)
        // [padding 4]
        // m_info.m_eState:      offset 184 (int,    4 bytes)  [8 + 176]
        // m_info.m_eEndReason:  offset 188 (int,    4 bytes)  [8 + 180]
        // m_info.m_szEndDebug:  offset 192 (char[], 128 bytes)[8 + 184]
        // m_eOldState:          offset 704 (int,    4 bytes)
        // ── SteamNetConnectionInfo_t field offsets ────────────────────────────────
        // m_hListenSocket: offset 144 (uint, 4 bytes)
        internal static uint GetConnectionListenSocket(IntPtr iface, uint hConn)
        {
            IntPtr buf = Marshal.AllocHGlobal(700); // 696 bytes + 4 bytes padding to avoid any potential overread
            try
            {
                SteamAPI_ISteamNetworkingSockets_GetConnectionInfo(iface, hConn, buf);
                return (uint)Marshal.ReadInt32(buf, 144);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        internal static uint           Cb_hConn(IntPtr p)      => (uint)Marshal.ReadInt32(p, 0);
        internal static ConnectionState Cb_eState(IntPtr p)    => (ConnectionState)Marshal.ReadInt32(p, 184);
        internal static int            Cb_endReason(IntPtr p)  => Marshal.ReadInt32(p, 188);
        internal static ConnectionState Cb_eOldState(IntPtr p) => (ConnectionState)Marshal.ReadInt32(p, 704);
        internal static string         Cb_endDebug(IntPtr p)   => Marshal.PtrToStringAnsi(p + 192) ?? "";
    }
}
