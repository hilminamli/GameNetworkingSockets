using System;

namespace GameNetworkingSockets.Transport
{
    /// <summary>Represents a single active connection. From the server's perspective, one connected client.</summary>
    public interface IConnection
    {
        /// <summary>Unique identifier for this connection.</summary>
        string Id { get; }

        bool IsConnected { get; }

        /// <summary>Sends raw bytes to the remote peer.</summary>
        void Send(byte[] data, SendType sendType = SendType.Reliable);

        /// <summary>Closes this connection.</summary>
        void Disconnect();

        /// <summary>Fired when a message is received from the remote peer.</summary>
        event Action<byte[]> OnMessage;

        /// <summary>Fired when the connection is closed by either side.</summary>
        event Action OnDisconnected;

        /// <summary>Returns real-time connection stats. Returns false if unavailable.</summary>
        bool GetConnectionStatus(out int pingMs, out float packetLoss);
    }
}
