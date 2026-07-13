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

        // P2P (custom signaling) mode — null when dialing by IP.
        private readonly SignalingPump _pump;
        private readonly string _peerIdentity;
        private readonly int _remoteVirtualPort;

        // Clients never receive NEW connect requests over signaling; reply blobs for the
        // in-progress connection are routed internally by GNS before this would be consulted.
        private static readonly FnCustomSignalingOnConnectRequest _ignoreConnectRequests =
            (IntPtr ctx, uint hConn, ref SteamNetworkingIdentity peer, int vport) => IntPtr.Zero;

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
            : this(messageBufferSize)
        {
            _address = address;
            _port    = port;
        }

        private ClientTransport(ISignalingChannel channel, string peerIdentity, int remoteVirtualPort,
            int messageBufferSize)
            : this(messageBufferSize)
        {
            _peerIdentity      = peerIdentity;
            _remoteVirtualPort = remoteVirtualPort;
            _pump              = new SignalingPump(channel, _ignoreConnectRequests);
        }

        private ClientTransport(int messageBufferSize)
        {
            _client   = new NetworkingClient(messageBufferSize);
            _dispatch = (_, data) => OnMessage?.Invoke(data);

            _client.OnConnected    += () => OnConnected?.Invoke();
            _client.OnDisconnected += (reason, debug) => OnDisconnected?.Invoke();
        }

        /// <summary>
        /// Creates a P2P transport that connects by identity through custom signaling instead of
        /// dialing an address. Rendezvous blobs travel over <paramref name="channel"/>; the actual
        /// traffic goes direct (ICE) once negotiated. Requires the library to be initialized with
        /// an explicit local identity and <see cref="NetworkingConfigValue.P2P_Transport_ICE_Enable"/>
        /// configured (the open-source build defaults to disabled).
        /// </summary>
        /// <param name="channel">Signaling side channel (e.g. the lobby connection).</param>
        /// <param name="peerIdentity">Generic identity string of the host to connect to.</param>
        /// <param name="remoteVirtualPort">Virtual port the host listens on; must match its localVirtualPort.</param>
        /// <param name="messageBufferSize">Max messages drained per <see cref="Tick"/>.</param>
        public static ClientTransport P2P(ISignalingChannel channel, string peerIdentity,
            int remoteVirtualPort = 0,
            int messageBufferSize = NetworkingConstants.DefaultClientMessageBufferSize)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (string.IsNullOrEmpty(peerIdentity))
                throw new ArgumentException("Peer identity must be non-empty.", nameof(peerIdentity));
            return new ClientTransport(channel, peerIdentity, remoteVirtualPort, messageBufferSize);
        }

        /// <summary>
        /// Initiates the connection — dials the configured address, or in P2P mode starts the
        /// custom-signaling rendezvous toward the configured peer identity.
        /// </summary>
        public bool Connect()
        {
            if (_pump == null)
                return _client.Connect($"{_address}:{_port}");

            // GNS takes ownership of the signaling object per connection — fresh one per attempt.
            IntPtr signaling = _pump.CreateSignalingObject();
            if (signaling == IntPtr.Zero) return false;
            var identity = P2PSignaling.MakeGenericIdentity(_peerIdentity);
            return _client.ConnectP2PCustomSignaling(signaling, ref identity, _remoteVirtualPort);
        }

        /// <summary>Closes the active connection.</summary>
        public void Disconnect() => _client.Disconnect();

        /// <summary>Runs GNS callbacks and fires OnMessage for each received packet. In P2P mode also feeds queued signaling blobs into GNS first.</summary>
        public void Tick()
        {
            _pump?.Drain();
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

        /// <summary>Returns the full real-time stats block for the active connection. Returns false if not connected.</summary>
        public bool GetConnectionStats(out ConnectionStats stats)
        {
            if (_client.Connection == 0) { stats = default; return false; }
            return _client.GetConnectionStats(_client.Connection, out stats);
        }

        public void Dispose() => _client.Dispose();
    }
}
