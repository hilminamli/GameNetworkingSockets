using System;

namespace GameNetworkingSockets.Transport
{
    /// <summary>IClientTransport implementation over GameNetworkingSockets.</summary>
    public sealed class ClientTransport : IClientTransport
    {
        private readonly string _address;
        private readonly ushort _port;
        private readonly NetworkingClient _client;
        private readonly MessageReceivedCallback _dispatch;

        /// <summary>True when the connection is in Connected state.</summary>
        public bool IsConnected => _client.IsConnected;

        /// <summary>Fired when the connection reaches Connected state.</summary>
        public event Action OnConnected;
        /// <summary>Fired when the connection closes for any reason.</summary>
        public event Action OnDisconnected;
        /// <summary>Fired for each received message. The span is only valid inside the handler — copy out anything you need to retain.</summary>
        public event MessageHandler OnMessage;

        /// <summary>Creates a ClientTransport targeting the given address and port.</summary>
        /// <param name="address">Server hostname or IP.</param>
        /// <param name="port">Server UDP port.</param>
        /// <param name="messageBufferSize">
        /// Max messages drained per <see cref="Tick"/>. Defaults to
        /// <see cref="NetworkingConstants.DefaultClientMessageBufferSize"/>. Increase if the server pushes
        /// large bursts (e.g. world snapshots) and a single tick should drain more of them at once.
        /// </param>
        public ClientTransport(string address, ushort port,
            int messageBufferSize = NetworkingConstants.DefaultClientMessageBufferSize)
        {
            _address  = address;
            _port     = port;
            _client   = new NetworkingClient(messageBufferSize);
            _dispatch = (_, data) => OnMessage?.Invoke(data);

            _client.OnConnected    += () => OnConnected?.Invoke();
            _client.OnDisconnected += (reason, debug) => OnDisconnected?.Invoke();
        }

        /// <summary>Initiates a connection to the configured server address.</summary>
        public bool Connect() => _client.Connect($"{_address}:{_port}");

        /// <summary>Closes the active connection.</summary>
        public void Disconnect() => _client.Disconnect();

        /// <summary>Runs GNS callbacks and fires OnMessage for each received packet.</summary>
        public void Tick()
        {
            _client.RunCallbacks();
            _ = _client.ReceiveMessages(_dispatch);
        }

        /// <summary>Sends raw data to the server.</summary>
        public void Send(ReadOnlySpan<byte> data, SendType sendType = SendType.Reliable)
            => _client.SendMessage(data, sendType);

        /// <summary>Returns ping and packet loss for the active connection. Returns false if not connected.</summary>
        public bool GetConnectionStatus(out int pingMs, out float packetLoss)
        {
            if (_client.Connection == 0) { pingMs = 0; packetLoss = 0f; return false; }
            return _client.GetConnectionStatus(_client.Connection, out pingMs, out packetLoss);
        }

        public void Dispose() => _client.Dispose();
    }
}
