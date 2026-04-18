using System;

namespace GameNetworkingSockets.Transport
{
    /// <summary>IConnection representing a single client connected to a ServerTransport.</summary>
    internal sealed class ServerConnection : IConnection
    {
        private readonly uint _hConn;
        private readonly ServerTransport _transport;

        public string Id { get; }
        public bool IsConnected { get; private set; } = true;

        public event Action<byte[]> OnMessage;
        public event Action OnDisconnected;

        internal ServerConnection(uint hConn, ServerTransport transport)
        {
            _hConn     = hConn;
            _transport = transport;
            Id         = hConn.ToString();
        }

        public void Send(byte[] data, SendType sendType = SendType.Reliable)
            => _transport.SendTo(_hConn, data, sendType);

        public void Disconnect()
        {
            IsConnected = false;
            _transport.Kick(_hConn);
        }

        public bool GetConnectionStatus(out int pingMs, out float packetLoss)
            => _transport.GetConnectionStatus(_hConn, out pingMs, out packetLoss);

        internal void DispatchMessage(byte[] data) => OnMessage?.Invoke(data);

        internal void NotifyDisconnected()
        {
            IsConnected = false;
            OnDisconnected?.Invoke();
        }
    }
}
