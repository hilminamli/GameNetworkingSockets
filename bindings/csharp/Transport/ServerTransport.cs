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

        /// <summary>Creates the listen socket and subscribes to GNS connection events.</summary>
        public void Start()
        {
            _server = new NetworkingServer(_port, _messageBufferSize);

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

        /// <summary>Runs GNS callbacks and dispatches received messages to their connections.</summary>
        public void Tick()
        {
            if (_server == null) return;

            _server.RunCallbacks();
            _ = _server.ReceiveMessages(_dispatch);
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
    }
}
