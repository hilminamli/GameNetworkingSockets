using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameNetworkingSockets
{
    /// <summary>GNS server socket. Listens for incoming connections and manages connected clients via a poll group.</summary>
    public class NetworkingServer : NetworkingSockets
    {
        private uint _listenSocket;
        private uint _pollGroup;

        private readonly List<uint>               _clients   = new List<uint>();
        private readonly ReadOnlyCollection<uint> _clientsRO;

        /// <summary>Snapshot-safe read-only list of currently connected client connection handles.</summary>
        public IReadOnlyList<uint> Clients => _clientsRO;

        /// <summary>Fired when a client reaches Connected state. Param: hConn.</summary>
        public event Action<uint>?              OnClientConnected;
        /// <summary>Fired when a client disconnects. Params: hConn, endReason, endDebug.</summary>
        public event Action<uint, int, string>? OnClientDisconnected;

        // ── Construction ─────────────────────────────────────────────────────────

        /// <summary>Creates a listen socket on the given port and prepares a poll group for receiving messages.</summary>
        public NetworkingServer(ushort port)
        {
            _clientsRO = new ReadOnlyCollection<uint>(_clients);

            var addr = new SteamNetworkingIPAddr();
            Native.SteamAPI_SteamNetworkingIPAddr_Clear(ref addr);
            addr.port = port;

            _listenSocket = Native.SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(
                _iface, ref addr, 0, IntPtr.Zero);

            if (_listenSocket == 0)
                throw new InvalidOperationException($"Failed to create listen socket on port {port}.");

            _pollGroup = CreatePollGroup();
        }

        // ── Dispatcher hooks ─────────────────────────────────────────────────────

        internal override bool OwnsConnection(uint hConn)
            => _clients.Contains(hConn)
            || Native.GetConnectionListenSocket(_iface, hConn) == _listenSocket;

        internal override void HandleStatusChanged(ConnectionStatusInfo info)
        {
            switch (info.state)
            {
                case ConnectionState.Connecting:
                    AcceptConnection(info.connection);
                    break;

                case ConnectionState.Connected:
                    _clients.Add(info.connection);
                    SetConnectionPollGroup(info.connection, _pollGroup);
                    OnClientConnected?.Invoke(info.connection);
                    break;

                case ConnectionState.ClosedByPeer:
                case ConnectionState.ProblemDetectedLocally:
                    _clients.Remove(info.connection);
                    OnClientDisconnected?.Invoke(info.connection, info.endReason, info.endDebug);
                    break;
            }
        }

        // ── Server API ───────────────────────────────────────────────────────────

        /// <summary>Accepts an incoming connection request. Automatically called on Connecting state change.</summary>
        public EResult AcceptConnection(uint hConn)
            => (EResult)Native.SteamAPI_ISteamNetworkingSockets_AcceptConnection(_iface, hConn);

        /// <summary>Receives pending messages from all connected clients via the poll group.</summary>
        public int ReceiveMessages(NetworkMessage[] messages)
            => ReceiveMessagesOnPollGroup(_pollGroup, messages);

        /// <summary>Sends data to all currently connected clients.</summary>
        public void Broadcast(byte[] data, SendType sendType = SendType.Reliable)
        {
            foreach (uint hConn in _clients.ToArray())
                SendMessage(hConn, data, sendType);
        }

        /// <summary>Closes a client connection, optionally with a reason code and debug message.</summary>
        public void KickClient(uint hConn, int reason = 0, string? debug = null)
            => CloseConnection(hConn, reason, debug);

        // ── IDisposable ──────────────────────────────────────────────────────────

        public override void Dispose()
        {
            foreach (uint hConn in _clients.ToArray())
                CloseConnection(hConn);
            _clients.Clear();

            if (_pollGroup != 0)
            {
                DestroyPollGroup(_pollGroup);
                _pollGroup = 0;
            }

            if (_listenSocket != 0)
            {
                Native.SteamAPI_ISteamNetworkingSockets_CloseListenSocket(_iface, _listenSocket);
                _listenSocket = 0;
            }

            base.Dispose(); // sets _disposed, unregisters from library
        }

        ~NetworkingServer()
        {
            // Dispose was not called — clean up native resources.
            // Only close the listen socket; client connections are not touched because
            // GNS automatically drops them when the listen socket closes.
            if (_listenSocket != 0 && NetworkingLibrary.IsInitialized)
                Native.SteamAPI_ISteamNetworkingSockets_CloseListenSocket(_iface, _listenSocket);
        }
    }
}
