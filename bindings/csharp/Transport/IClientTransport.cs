using System;

namespace GameNetworkingSockets.Transport
{
    /// <summary>Client-side transport. Manages a single outbound connection to a server.</summary>
    public interface IClientTransport : IDisposable
    {
        bool IsConnected { get; }

        /// <summary>Initiates the connection to the configured server address.</summary>
        bool Connect();

        /// <summary>Closes the active connection.</summary>
        void Disconnect();

        /// <summary>Processes pending callbacks and dispatches received messages. Call once per tick.</summary>
        void Tick();

        /// <summary>Sends raw bytes to the server.</summary>
        void Send(byte[] data, SendType sendType = SendType.Reliable);

        /// <summary>Fired when the connection is established.</summary>
        event Action OnConnected;

        /// <summary>Fired when the connection is closed.</summary>
        event Action OnDisconnected;

        /// <summary>Fired when a message is received from the server.</summary>
        event Action<byte[]> OnMessage;

        /// <summary>Returns real-time connection stats. Returns false if unavailable.</summary>
        bool GetConnectionStatus(out int pingMs, out float packetLoss);
    }
}
