using System;

namespace GameNetworkingSockets.Transport
{
    /// <summary>Server-side transport. Listens for incoming connections and delivers raw messages.</summary>
    public interface IServerTransport : IDisposable
    {
        /// <summary>Starts the listen socket.</summary>
        void Start();

        /// <summary>Stops the server and closes all connections.</summary>
        void Stop();

        /// <summary>Processes pending callbacks and dispatches received messages. Call once per tick.</summary>
        void Tick();

        /// <summary>Fired when a new client connects.</summary>
        event Action<IConnection> OnConnected;

        /// <summary>Fired when a client disconnects.</summary>
        event Action<IConnection> OnDisconnected;

        /// <summary>Sends data to all connected clients. Zero allocation.</summary>
        void Broadcast(ReadOnlySpan<byte> data, SendType sendType = SendType.Reliable);
    }
}
