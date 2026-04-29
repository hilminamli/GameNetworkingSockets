using System;
using System.Text;

namespace GameNetworkingSockets
{
    /// <summary>Manages GNS library lifetime and routes connection status callbacks to the owning socket instance.</summary>
    public static class NetworkingLibrary
    {
        private static IntPtr _utils;
        private static bool   _initialized;

        // Delegate references kept alive so GC never collects them while native code holds them
        private static FnConnectionStatusChanged _dispatchDelegate;
        private static FnDebugOutput             _debugDelegate;

        private static volatile NetworkingSockets[] _snapshot = Array.Empty<NetworkingSockets>();
        private static readonly object _lock = new object();

        // ── Init / Kill ──────────────────────────────────────────────────────────

        /// <summary>Initializes the GNS library and registers the global connection status callback. Must be called before creating any socket.</summary>
        public static bool Initialize(out string error)
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
            _ = Native.SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged(
                _utils, _dispatchDelegate);

            return true;
        }

        /// <summary>Shuts down the GNS library and clears all registered socket instances.</summary>
        public static void Kill()
        {
            if (!_initialized) return;
            _initialized = false;

            lock (_lock)
                _snapshot = Array.Empty<NetworkingSockets>();

            Native.GameNetworkingSockets_Kill();
        }

        public static bool IsInitialized => _initialized;
        internal static IntPtr Utils      => _utils;

        // ── Global config ────────────────────────────────────────────────────────

        /// <summary>
        /// Sets a GNS int32 configuration value at global scope. Call after <see cref="Initialize"/>.
        /// Connection-scoped settings (e.g. <see cref="NetworkingConfigValue.SendRateMax"/>) propagate
        /// as defaults to every connection created after the call; existing connections are unaffected.
        /// </summary>
        /// <remarks>
        /// This binding does not impose its own defaults — every value reflects GNS native defaults until
        /// the caller overrides it. Throughput, buffering, and timeout characteristics are application-
        /// specific decisions; tune deliberately.
        /// </remarks>
        /// <returns>True on success. False if GNS rejected the value (e.g. out of range, wrong scope, wrong data type).</returns>
        public static bool SetGlobalConfig(NetworkingConfigValue value, int int32Value)
        {
            if (!_initialized)
                throw new InvalidOperationException("Call NetworkingLibrary.Initialize() first.");

            return Native.SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueInt32(
                _utils, (int)value, int32Value);
        }

        // ── Debug ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Routes GNS internal debug messages to a <see cref="BufferedDebugLog"/>. Native invokes
        /// the callback on a GNS worker thread, so the buffer's bounded enqueue is the only
        /// thread-safe path. Drain the buffer on the main thread (e.g. each tick) via
        /// <see cref="BufferedDebugLog.Drain"/>.
        /// </summary>
        /// <param name="level">Minimum severity GNS should report.</param>
        /// <param name="log">Buffer that captures messages. Must remain alive while logging is active.</param>
        public static void SetDebugOutput(DebugOutputType level, BufferedDebugLog log)
        {
            if (log == null) throw new ArgumentNullException(nameof(log));

            var oldDelegate = _debugDelegate;
            var newDelegate = new FnDebugOutput((nType, pszMsg) =>
                log.Enqueue((DebugOutputType)nType, Native.PtrToStringUtf8(pszMsg, 4096)));

            // Point native at the new thunk first, then drop the old field reference.
            // GC.KeepAlive keeps the old delegate alive across the native transition so
            // any in-flight invocation on a GNS worker thread completes safely.
            Native.SteamAPI_ISteamNetworkingUtils_SetDebugOutputFunction(
                _utils, (int)level, newDelegate);
            _debugDelegate = newDelegate;
            GC.KeepAlive(oldDelegate);
        }

        // ── Instance registry ────────────────────────────────────────────────────

        internal static void Register(NetworkingSockets instance)
        {
            lock (_lock)
            {
                var current = _snapshot;
                var next = new NetworkingSockets[current.Length + 1];
                current.CopyTo(next, 0);
                next[current.Length] = instance;
                _snapshot = next;
            }
        }

        internal static void Unregister(NetworkingSockets instance)
        {
            lock (_lock)
            {
                var current = _snapshot;
                if (Array.IndexOf(current, instance) < 0) return;
                var next = new NetworkingSockets[current.Length - 1];
                int j = 0;
                foreach (var s in current)
                    if (s != instance) next[j++] = s;
                _snapshot = next;
            }
        }

        // ── Global dispatcher ────────────────────────────────────────────────────

        private static void Dispatch(IntPtr pInfo)
        {
            if (!_initialized) return;

            uint hConn = Native.Cb_hConn(pInfo);

            foreach (var instance in _snapshot)
            {
                if (instance.OwnsConnection(hConn))
                {
                    var state = Native.Cb_eState(pInfo);
                    instance.HandleStatusChanged(new ConnectionStatusInfo
                    {
                        connection = hConn,
                        state      = state,
                        oldState   = Native.Cb_eOldState(pInfo),
                        endReason  = Native.Cb_endReason(pInfo),
                        endDebug   = (state == ConnectionState.ClosedByPeer || state == ConnectionState.ProblemDetectedLocally)
                                         ? Native.Cb_endDebug(pInfo)
                                         : null,
                    });
                    return;
                }
            }
        }
    }
}
