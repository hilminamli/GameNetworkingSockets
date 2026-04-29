using System;

namespace GameNetworkingSockets
{
    /// <summary>GNS client socket. Connects to a remote server and handles send/receive for a single connection.</summary>
    public class NetworkingClient : NetworkingSockets
    {
        /// <summary>GNS connection handle. Zero when not connected.</summary>
        public uint Connection  { get; private set; }
        /// <summary>True when the connection is in Connected state.</summary>
        public bool IsConnected { get; private set; }

        /// <summary>Fired when the connection reaches Connected state.</summary>
        public event Action               OnConnected;
        /// <summary>Fired on disconnect. Params: endReason, endDebug.</summary>
        public event Action<int, string>  OnDisconnected;

        // ── Construction ─────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a client socket. Connect with <see cref="Connect(string)"/> or <see cref="Connect(ref SteamNetworkingIPAddr)"/>.
        /// </summary>
        /// <param name="messageBufferSize">
        /// Max messages drained per <see cref="ReceiveMessages"/> call. Defaults to
        /// <see cref="NetworkingConstants.DefaultClientMessageBufferSize"/>. Increase if the server pushes
        /// large bursts (e.g. world snapshots) and a single tick should drain more of them at once.
        /// </param>
        public NetworkingClient(int messageBufferSize = NetworkingConstants.DefaultClientMessageBufferSize)
            : base(messageBufferSize) { }

        // ── Dispatcher hooks ─────────────────────────────────────────────────────

        internal override bool OwnsConnection(uint hConn)
            => hConn == Connection;

        internal override void HandleStatusChanged(ConnectionStatusInfo info)
        {
            switch (info.state)
            {
                case ConnectionState.Connected:
                    IsConnected = true;
                    OnConnected?.Invoke();
                    break;

                case ConnectionState.ClosedByPeer:
                case ConnectionState.ProblemDetectedLocally:
                    IsConnected = false;
                    OnDisconnected?.Invoke(info.endReason, info.endDebug);
                    // GNS contract: connection is already closed on the wire, but the handle
                    // still holds local resources until CloseConnection is called.
                    _ = CloseConnection(info.connection);
                    Connection = 0;
                    break;

                case ConnectionState.None:
                case ConnectionState.Connecting:
                case ConnectionState.FindingRoute:
                default:
                    break;
            }
        }

        // ── Client API ───────────────────────────────────────────────────────────

        /// <summary>Initiates a connection to the given "ip:port" string. Throws on invalid address format.</summary>
        public bool Connect(string ipAndPort)
        {
            var addr = new SteamNetworkingIPAddr();
            if (!Native.SteamAPI_SteamNetworkingIPAddr_ParseString(ref addr, ipAndPort))
                throw new ArgumentException($"Invalid address: {ipAndPort}");

            return Connect(ref addr);
        }

        /// <summary>Initiates a connection to the given address struct.</summary>
        public bool Connect(ref SteamNetworkingIPAddr address)
        {
            Connection = Native.SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress(
                _iface, ref address, 0, IntPtr.Zero);

            return Connection != 0;
        }

        /// <summary>Closes the active connection and resets connection state.</summary>
        public bool Disconnect(int reason = 0, string debug = null)
        {
            if (Connection == 0) return false;

            bool ok     = CloseConnection(Connection, reason, debug);
            IsConnected = false;
            Connection  = 0;
            return ok;
        }

        /// <summary>Sends data to the active server connection.</summary>
        public EResult SendMessage(ReadOnlySpan<byte> data, SendType sendType = SendType.Reliable)
            => SendMessage(Connection, data, sendType);

        /// <summary>Receives pending messages from the server and dispatches each to the callback.</summary>
        public int ReceiveMessages(MessageReceivedCallback receiver)
            => ReceiveMessages(Connection, receiver);

        // ── IDisposable ──────────────────────────────────────────────────────────

        public override void Dispose()
        {
            _ = Disconnect();
            base.Dispose(); // sets _disposed, unregisters from library
        }

        ~NetworkingClient()
        {
            // Dispose was not called — clean up native connection
            if (Connection != 0 && NetworkingLibrary.IsInitialized)
                _ = Native.SteamAPI_ISteamNetworkingSockets_CloseConnection(_iface, Connection, 0, null, false);
        }
    }
}
