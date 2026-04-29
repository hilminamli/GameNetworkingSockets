using System;

namespace GameNetworkingSockets.Transport
{
    /// <summary>Handler for an incoming message. The span is only valid for the duration of the call — copy out anything you need to retain.</summary>
    public delegate void MessageHandler(ReadOnlySpan<byte> data);

    /// <summary>Represents a single active connection. From the server's perspective, one connected client.</summary>
    public interface IConnection
    {
        /// <summary>Unique identifier for this connection.</summary>
        string Id { get; }

        bool IsConnected { get; }

        /// <summary>Sends raw bytes to the remote peer.</summary>
        void Send(ReadOnlySpan<byte> data, SendType sendType = SendType.Reliable);

        /// <summary>Closes this connection.</summary>
        void Disconnect();

        /// <summary>
        /// Fired when a message is received. The span is only valid inside the handler — do not
        /// capture it, cross an await with it, or hand it to another thread. Copy out anything you need.
        /// </summary>
        event MessageHandler OnMessage;

        /// <summary>Fired when the connection is closed by either side.</summary>
        event Action OnDisconnected;

        /// <summary>Returns real-time connection stats. Returns false if unavailable.</summary>
        bool GetConnectionStatus(out int pingMs, out float packetLoss);
    }
}
