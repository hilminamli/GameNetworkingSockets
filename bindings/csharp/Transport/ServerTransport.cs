using System;
using System.Collections.Generic;

namespace GameNetworkingSockets.Transport
{
    /// <summary>IServerTransport implementation over GameNetworkingSockets.</summary>
    public sealed class ServerTransport : IServerTransport
    {
        private readonly ushort _port;
        private NetworkingServer _server;
        private readonly Dictionary<uint, ServerConnection> _connections = new Dictionary<uint, ServerConnection>();
        private readonly NetworkMessage[] _msgBuffer = new NetworkMessage[NetworkingConstants.MessageBufferSize];

        /// <summary>Fired when a new client connects.</summary>
        public event Action<IConnection> OnConnected;
        /// <summary>Fired when a client disconnects.</summary>
        public event Action<IConnection> OnDisconnected;

        /// <summary>Creates a ServerTransport that will listen on the given port.</summary>
        public ServerTransport(ushort port) => _port = port;

        /// <summary>Creates the listen socket and subscribes to GNS connection events.</summary>
        public void Start()
        {
            _server = new NetworkingServer(_port);

            _server.OnClientConnected += hConn =>
            {
                var conn = new ServerConnection(hConn, this);
                _connections[hConn] = conn;
                OnConnected?.Invoke(conn);
            };

            _server.OnClientDisconnected += (hConn, reason, debug) =>
            {
                if (!_connections.TryGetValue(hConn, out var conn)) return;
                _connections.Remove(hConn);
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

            int count = _server.ReceiveMessages(_msgBuffer);
            for (int i = 0; i < count; i++)
            {
                if (_connections.TryGetValue(_msgBuffer[i].connection, out var conn))
                    conn.DispatchMessage(_msgBuffer[i].data);
            }
        }

        /// <inheritdoc cref="Stop"/>
        public void Dispose() => Stop();

        /// <summary>Sends data to all currently connected clients.</summary>
        public void Broadcast(byte[] data, SendType sendType = SendType.Reliable)
            => _server?.Broadcast(data, sendType);

        internal void SendTo(uint hConn, byte[] data, SendType sendType)
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
