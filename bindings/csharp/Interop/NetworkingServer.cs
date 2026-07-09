using System;
using System.Buffers;
using System.Collections.Generic;

namespace GameNetworkingSockets
{
    /// <summary>GNS server socket. Listens for incoming connections and manages connected clients via a poll group.</summary>
    public class NetworkingServer : NetworkingSockets
    {
        private uint _listenSocket;
        private uint _pollGroup;

        private readonly HashSet<uint> _clients = new HashSet<uint>();

        /// <summary>Read-only view of currently connected client connection handles.</summary>
        public IReadOnlyCollection<uint> Clients => _clients;

        /// <summary>Fired when a client reaches Connected state. Param: hConn.</summary>
        public event Action<uint> OnClientConnected;
        /// <summary>Fired when a client disconnects. Params: hConn, endReason, endDebug.</summary>
        public event Action<uint, int, string> OnClientDisconnected;

        // ── Construction ─────────────────────────────────────────────────────────

        /// <summary>Creates a listen socket on the given port and prepares a poll group for receiving messages.</summary>
        /// <param name="port">UDP port to listen on.</param>
        /// <param name="messageBufferSize">
        /// Max messages drained per <see cref="ReceiveMessages"/> call across the poll group. Defaults to
        /// <see cref="NetworkingConstants.DefaultServerMessageBufferSize"/>. Tune up for high client counts
        /// or burst traffic so a single tick can drain more pending messages without leaving them queued
        /// for the next tick.
        /// </param>
        public NetworkingServer(ushort port, int messageBufferSize = NetworkingConstants.DefaultServerMessageBufferSize)
            : base(messageBufferSize)
        {
            var addr = new SteamNetworkingIPAddr();
            Native.SteamAPI_SteamNetworkingIPAddr_Clear(ref addr);
            addr.port = port;

            _listenSocket = Native.SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP(
                _iface, ref addr, 0, IntPtr.Zero);

            if (_listenSocket == 0)
                throw new InvalidOperationException($"Failed to create listen socket on port {port}.");

            _pollGroup = CreatePollGroup();
        }

        /// <summary>
        /// Creates a P2P listen socket (accepts ICE / SDR / custom-signaling connections) instead of an
        /// IP listener. Peers reach this host by identity, not IP. Requires the native library to be built
        /// with ENABLE_ICE (+ USE_STEAMWEBRTC for real NAT traversal). Use the <see cref="P2P"/> factory to
        /// disambiguate from the IP constructor.
        /// </summary>
        /// <param name="localVirtualPort">Virtual port peers connect to; must match their remoteVirtualPort. Defaults to 0.</param>
        /// <param name="messageBufferSize">Max messages drained per <see cref="ReceiveMessages"/> call across the poll group.</param>
        private NetworkingServer(int localVirtualPort, int messageBufferSize)
            : base(messageBufferSize)
        {
            _listenSocket = Native.SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P(
                _iface, localVirtualPort, 0, IntPtr.Zero);

            if (_listenSocket == 0)
                throw new InvalidOperationException($"Failed to create P2P listen socket on virtual port {localVirtualPort}.");

            _pollGroup = CreatePollGroup();
        }

        /// <summary>Creates a P2P server listening on the given virtual port (identity-based, not IP).</summary>
        public static NetworkingServer P2P(int localVirtualPort = 0, int messageBufferSize = NetworkingConstants.DefaultServerMessageBufferSize)
            => new NetworkingServer(localVirtualPort, messageBufferSize);

        // ── Dispatcher hooks ─────────────────────────────────────────────────────

        internal override bool OwnsConnection(uint hConn)
            => _clients.Contains(hConn)
            || Native.GetConnectionListenSocket(_iface, hConn) == _listenSocket;

        internal override void HandleStatusChanged(ConnectionStatusInfo info)
        {
            switch (info.state)
            {
                case ConnectionState.Connecting:
                    _ = AcceptConnection(info.connection);
                    break;

                case ConnectionState.Connected:
                    _ = _clients.Add(info.connection);
                    _ = SetConnectionPollGroup(info.connection, _pollGroup);
                    OnClientConnected?.Invoke(info.connection);
                    break;

                case ConnectionState.ClosedByPeer:
                case ConnectionState.ProblemDetectedLocally:
                    _ = _clients.Remove(info.connection);
                    OnClientDisconnected?.Invoke(info.connection, info.endReason, info.endDebug);
                    // GNS contract: the connection is already closed on the wire, but the handle
                    // still holds local resources until CloseConnection is called. Release it now
                    // that subscribers have observed the disconnect.
                    _ = CloseConnection(info.connection);
                    break;

                case ConnectionState.None:
                case ConnectionState.FindingRoute:
                default:
                    break;
            }
        }

        // ── Server API ───────────────────────────────────────────────────────────

        /// <summary>Accepts an incoming connection request. Automatically called on Connecting state change.</summary>
        public EResult AcceptConnection(uint hConn)
            => (EResult)Native.SteamAPI_ISteamNetworkingSockets_AcceptConnection(_iface, hConn);

        /// <summary>
        /// Adopts an incoming custom-signaling P2P connection (call from an OnConnectRequest
        /// callback with the hConn it received). Custom-signaling connections are not associated
        /// with any listen socket, so their status callbacks would never route to this server —
        /// adoption registers the handle first (so subsequent callbacks route here) and then
        /// accepts it. The Connected state change attaches it to the poll group as usual.
        /// </summary>
        public EResult AdoptP2PConnection(uint hConn)
        {
            _ = _clients.Add(hConn);
            var result = AcceptConnection(hConn);
            if (result != EResult.OK)
                _ = _clients.Remove(hConn);
            return result;
        }

        /// <summary>Receives pending messages from all connected clients via the poll group and dispatches each to the callback.</summary>
        public int ReceiveMessages(MessageReceivedCallback receiver)
            => ReceiveMessagesOnPollGroup(_pollGroup, receiver);

        /// <summary>Sends data to all currently connected clients via a single batched P/Invoke call.</summary>
        public unsafe void Broadcast(ReadOnlySpan<byte> data, SendType sendType = SendType.Reliable)
        {
            int count = _clients.Count;
            if (count == 0 || data.IsEmpty) return;

            IntPtr[] msgs = ArrayPool<IntPtr>.Shared.Rent(count);
            try
            {
                int allocated = 0;
                IntPtr utils = NetworkingLibrary.Utils;
                foreach (uint hConn in _clients)
                {
                    IntPtr m = Native.SteamAPI_ISteamNetworkingUtils_AllocateMessage(utils, data.Length);
                    if (m == IntPtr.Zero) continue;
                    var msg = (Native.SteamNetworkingMessage_t*)m;
                    fixed (byte* src = data)
                        Buffer.MemoryCopy(src, (void*)msg->pData, data.Length, data.Length);
                    msg->conn = hConn;
                    msg->flags = (int)sendType;
                    msgs[allocated++] = m;
                }
                if (allocated > 0)
                    Native.SteamAPI_ISteamNetworkingSockets_SendMessages(_iface, allocated, msgs, IntPtr.Zero);
            }
            finally
            {
                ArrayPool<IntPtr>.Shared.Return(msgs);
            }
        }

        /// <summary>Closes a client connection, optionally with a reason code and debug message.</summary>
        public void KickClient(uint hConn, int reason = 0, string debug = null)
            => CloseConnection(hConn, reason, debug);

        // ── IDisposable ──────────────────────────────────────────────────────────

        public override void Dispose()
        {
            var snapshot = new uint[_clients.Count];
            _clients.CopyTo(snapshot);
            foreach (uint hConn in snapshot)
                _ = CloseConnection(hConn);
            _clients.Clear();

            if (_pollGroup != 0)
            {
                _ = DestroyPollGroup(_pollGroup);
                _pollGroup = 0;
            }

            if (_listenSocket != 0)
            {
                _ = Native.SteamAPI_ISteamNetworkingSockets_CloseListenSocket(_iface, _listenSocket);
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
                _ = Native.SteamAPI_ISteamNetworkingSockets_CloseListenSocket(_iface, _listenSocket);
        }
    }
}
