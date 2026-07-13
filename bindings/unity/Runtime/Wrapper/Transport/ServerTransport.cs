using System;
using System.Collections.Generic;

namespace GameNetworkingSockets.Transport
{
    /// <summary>IServerTransport implementation over GameNetworkingSockets.</summary>
    public sealed class ServerTransport : IServerTransport
    {
        private readonly ushort _port;
        private readonly int _messageBufferSize;
        private NetworkingServer _server;
        private readonly Dictionary<uint, ServerConnection> _connections = new Dictionary<uint, ServerConnection>();
        private readonly MessageReceivedCallback _dispatch;

        // P2P (custom signaling) mode — _pump is null when listening on a UDP port.
        private readonly SignalingPump _pump;
        private readonly int _localVirtualPort;
        private readonly FnCustomSignalingOnConnectRequest _onConnectRequest;

        /// <summary>Fired when a new client connects.</summary>
        public event Action<IConnection> OnConnected;
        /// <summary>Fired when a client disconnects.</summary>
        public event Action<IConnection> OnDisconnected;

        /// <summary>Creates a ServerTransport that will listen on the given port.</summary>
        /// <param name="port">UDP port to listen on.</param>
        /// <param name="messageBufferSize">
        /// Max messages drained per <see cref="Tick"/> across the poll group. Defaults to
        /// <see cref="NetworkingConstants.DefaultServerMessageBufferSize"/>. Tune up for high client counts
        /// or burst traffic so a single tick can drain more pending messages without leaving them queued
        /// for the next tick. Applied when <see cref="Start"/> creates the listen socket.
        /// </param>
        public ServerTransport(ushort port,
            int messageBufferSize = NetworkingConstants.DefaultServerMessageBufferSize)
        {
            _port              = port;
            _messageBufferSize = messageBufferSize;
            _dispatch = (hConn, data) =>
            {
                if (_connections.TryGetValue(hConn, out var conn))
                    conn.DispatchMessage(data);
            };
        }

        private ServerTransport(ISignalingChannel channel, int localVirtualPort, int messageBufferSize)
            : this(port: 0, messageBufferSize)
        {
            _localVirtualPort = localVirtualPort;
            _onConnectRequest = HandleConnectRequest;
            _pump             = new SignalingPump(channel, _onConnectRequest);
        }

        /// <summary>
        /// Creates a P2P transport that accepts connections by identity through custom signaling
        /// instead of a UDP port. Rendezvous blobs travel over <paramref name="channel"/>; actual
        /// traffic goes direct (ICE) once negotiated. Requires the library to be initialized with
        /// an explicit local identity and <see cref="NetworkingConfigValue.P2P_Transport_ICE_Enable"/>
        /// configured (the open-source build defaults to disabled).
        /// </summary>
        /// <param name="channel">Signaling side channel (e.g. the lobby connection).</param>
        /// <param name="localVirtualPort">Virtual port to listen on; peers connect with the same remoteVirtualPort.</param>
        /// <param name="messageBufferSize">Max messages drained per <see cref="Tick"/> across the poll group.</param>
        public static ServerTransport P2P(ISignalingChannel channel, int localVirtualPort = 0,
            int messageBufferSize = NetworkingConstants.DefaultServerMessageBufferSize)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            return new ServerTransport(channel, localVirtualPort, messageBufferSize);
        }

        /// <summary>Creates the listen socket and subscribes to GNS connection events.</summary>
        public void Start()
        {
            _server = _pump != null
                ? NetworkingServer.P2P(_localVirtualPort, _messageBufferSize)
                : new NetworkingServer(_port, _messageBufferSize);

            _server.OnClientConnected += hConn =>
            {
                var conn = new ServerConnection(hConn, this);
                _connections[hConn] = conn;
                OnConnected?.Invoke(conn);
            };

            _server.OnClientDisconnected += (hConn, reason, debug) =>
            {
                if (!_connections.TryGetValue(hConn, out var conn)) return;
                _ = _connections.Remove(hConn);
                conn.NotifyDisconnected();
                OnDisconnected?.Invoke(conn);
            };
        }

        /// <summary>Closes all connections and disposes the listen socket.</summary>
        public void Stop()
        {
            _server?.Dispose();
            _server = null;
            _connections.Clear();
        }

        /// <summary>Runs GNS callbacks and dispatches received messages to their connections. In P2P mode also feeds queued signaling blobs into GNS first.</summary>
        public void Tick()
        {
            if (_server == null) return;

            _pump?.Drain();
            _server.RunCallbacks();
            _ = _server.ReceiveMessages(_dispatch);
        }

        /// <summary>
        /// Runs inline from <see cref="SignalingPump.Drain"/> when an inbound blob announces a
        /// new connection. Custom-signaling connections are not tied to the listen socket, so
        /// the server adopts them explicitly; the returned signaling object carries the reply
        /// blobs for this connection back over the channel.
        /// </summary>
        private IntPtr HandleConnectRequest(IntPtr ctx, uint hConn, ref SteamNetworkingIdentity identityPeer, int nLocalVirtualPort)
        {
            if (_server == null || _server.AdoptP2PConnection(hConn) != EResult.OK)
                return IntPtr.Zero;
            return _pump.CreateSignalingObject();
        }

        /// <inheritdoc cref="Stop"/>
        public void Dispose() => Stop();

        /// <summary>Sends data to all currently connected clients. Zero allocation.</summary>
        public void Broadcast(ReadOnlySpan<byte> data, SendType sendType = SendType.Reliable)
            => _server?.Broadcast(data, sendType);

        internal void SendTo(uint hConn, ReadOnlySpan<byte> data, SendType sendType)
            => _server?.SendMessage(hConn, data, sendType);

        internal void Kick(uint hConn)
            => _server?.KickClient(hConn);

        internal bool GetConnectionStatus(uint hConn, out int pingMs, out float packetLoss)
        {
            if (_server != null) return _server.GetConnectionStatus(hConn, out pingMs, out packetLoss);
            pingMs = 0; packetLoss = 0f; return false;
        }

        internal bool GetConnectionStats(uint hConn, out ConnectionStats stats)
        {
            if (_server != null) return _server.GetConnectionStats(hConn, out stats);
            stats = default; return false;
        }
    }
}
