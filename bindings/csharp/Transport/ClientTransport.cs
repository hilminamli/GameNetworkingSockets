using System;

namespace GameNetworkingSockets.Transport
{
    /// <summary>IClientTransport implementation over GameNetworkingSockets.</summary>
    public sealed class ClientTransport : IClientTransport
    {
        private readonly string _address;
        private readonly ushort _port;
        private readonly NetworkingClient _client;
        private readonly NetworkMessage[] _msgBuffer = new NetworkMessage[NetworkingConstants.MessageBufferSize];

        /// <summary>True when the connection is in Connected state.</summary>
        public bool IsConnected => _client.IsConnected;

        /// <summary>Fired when the connection reaches Connected state.</summary>
        public event Action OnConnected;
        /// <summary>Fired when the connection closes for any reason.</summary>
        public event Action OnDisconnected;
        /// <summary>Fired for each received message with its raw payload.</summary>
        public event Action<byte[]> OnMessage;

        /// <summary>Creates a ClientTransport targeting the given address and port.</summary>
        public ClientTransport(string address, ushort port)
        {
            _address = address;
            _port    = port;
            _client  = new NetworkingClient();

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

            int count = _client.ReceiveMessages(_msgBuffer);
            for (int i = 0; i < count; i++)
                OnMessage?.Invoke(_msgBuffer[i].data);
        }

        /// <summary>Sends raw data to the server.</summary>
        public void Send(byte[] data, SendType sendType = SendType.Reliable)
            => _client.SendMessage(data, sendType);

        /// <summary>Returns ping and packet loss for the active connection. Returns false if not connected.</summary>
        public bool GetConnectionStatus(out int pingMs, out float packetLoss)
        {
            if (_client.Connection == 0) { pingMs = 0; packetLoss = 0f; return false; }
            return _client.GetConnectionStatus(_client.Connection, out pingMs, out packetLoss);
        }

        /// <inheritdoc cref="Disconnect"/>
        public void Dispose() => Disconnect();
    }
}
