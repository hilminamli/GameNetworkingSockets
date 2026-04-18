using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Valve.Sockets
{
    /// <summary>Manages GNS library lifetime and routes connection status callbacks to the owning socket instance.</summary>
    public static class NetworkingLibrary
    {
        private static IntPtr _utils;
        private static bool   _initialized;

        // Delegate references kept alive so GC never collects them while native code holds them
        private static FnConnectionStatusChanged? _dispatchDelegate;
        private static FnDebugOutput?             _debugDelegate;

        private static readonly List<NetworkingSockets> _instances = new List<NetworkingSockets>();
        private static readonly object _lock = new object();

        // ── Init / Kill ──────────────────────────────────────────────────────────

        /// <summary>Initializes the GNS library and registers the global connection status callback. Must be called before creating any socket.</summary>
        public static bool Initialize(out string? error)
        {
            var errBuf = new byte[1024];
            bool ok = Native.GameNetworkingSockets_Init(IntPtr.Zero, errBuf);
            error = ok ? null : Encoding.UTF8.GetString(errBuf).TrimEnd('\0');

            if (!ok) return false;

            _initialized = true;
            _utils = Native.SteamAPI_SteamNetworkingUtils_v003();

            // Assign before passing to native — prevents a race where native fires
            // the callback before the field is set
            _dispatchDelegate = Dispatch;
            Native.SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged(
                _utils, _dispatchDelegate);

            return true;
        }

        /// <summary>Shuts down the GNS library and clears all registered socket instances.</summary>
        public static void Kill()
        {
            if (!_initialized) return;
            _initialized = false;

            lock (_lock)
                _instances.Clear();

            Native.GameNetworkingSockets_Kill();
        }

        public static bool IsInitialized => _initialized;

        // ── Debug ────────────────────────────────────────────────────────────────

        /// <summary>Registers a debug output handler for GNS internal log messages at the given verbosity level.</summary>
        public static void SetDebugOutput(DebugOutputType level, Action<DebugOutputType, string> handler)
        {
            // Assign to field first so the old delegate stays alive until after the native call
            var newDelegate = new FnDebugOutput((nType, pszMsg) =>
                handler((DebugOutputType)nType, Marshal.PtrToStringAnsi(pszMsg) ?? ""));

            _debugDelegate = newDelegate;

            Native.SteamAPI_ISteamNetworkingUtils_SetDebugOutputFunction(
                _utils, (int)level, _debugDelegate);
        }

        // ── Instance registry ────────────────────────────────────────────────────

        internal static void Register(NetworkingSockets instance)
        {
            lock (_lock)
                _instances.Add(instance);
        }

        internal static void Unregister(NetworkingSockets instance)
        {
            lock (_lock)
                _instances.Remove(instance);
        }

        // ── Global dispatcher ────────────────────────────────────────────────────

        private static void Dispatch(IntPtr pInfo)
        {
            if (!_initialized) return;

            uint hConn = Native.Cb_hConn(pInfo);

            // Copy list under lock so Dispose on another thread can't mutate it mid-loop
            NetworkingSockets[] snapshot;
            lock (_lock)
                snapshot = _instances.ToArray();

            foreach (var instance in snapshot)
            {
                if (instance.OwnsConnection(hConn))
                {
                    instance.HandleStatusChanged(new ConnectionStatusInfo
                    {
                        connection = hConn,
                        state      = Native.Cb_eState(pInfo),
                        oldState   = Native.Cb_eOldState(pInfo),
                        endReason  = Native.Cb_endReason(pInfo),
                        endDebug   = Native.Cb_endDebug(pInfo),
                    });
                    return;
                }
            }
        }
    }
}
