using System;
using System.Collections.Generic;
using ThrowiaServer.Network;
using ThrowiaServer.Network.Serializer;

namespace ThrowiaServer.Network.GNSNetwork
{
    /// <summary>Represents a single connected client on the server side. Handles message routing, listener registration, and ping stats.</summary>
    public class GNSSession : IKeyDataSession
    {
        /// <summary>Unique identifier for this session, generated at construction. Stable for the lifetime of the session.</summary>
        public Guid SessionID { get; } = Guid.NewGuid();
        /// <summary>Set by GNSServer on connect/disconnect. Do not set manually.</summary>
        public bool IsConnected { get; internal set; }
        public INetworkSerializer Serializer { get; }

        public Action? OnConnected    { get; set; }
        public Action? OnDisconnected { get; set; }

        public int   LastPing    => _pingTracker.LastPing;
        public int   AveragePing => _pingTracker.AveragePing;
        public int   Jitter      => _pingTracker.Jitter;
        /// <summary>Packet loss ratio 0..1, where 0 = no loss. Updated by UpdateStats() each tick batch.</summary>
        public float PacketLoss  { get; private set; }

        private readonly PingTracker _pingTracker = new PingTracker();

        internal uint HConn { get; }

        private readonly GNSServer _server;

        private readonly Dictionary<AuthorizationScope, Dictionary<int, List<NetworkActionCaller>>> _messageListeners
            = new Dictionary<AuthorizationScope, Dictionary<int, List<NetworkActionCaller>>>();

        private readonly Dictionary<AuthorizationScope, Func<IKeyDataSession, object>> _sessionIdentifierProvider;

        internal GNSSession(uint hConn, GNSServer server, INetworkSerializer serializer,
            Dictionary<AuthorizationScope, Func<IKeyDataSession, object>> sessionIdentifierProvider)
        {
            HConn = hConn;
            _server = server;
            Serializer = serializer;
            _sessionIdentifierProvider = sessionIdentifierProvider;
        }

        // Called by GNSServer.Tick() — already on main thread, no queue needed
        internal void HandleMessage(byte[] data)
        {
            var message = Serializer.Deserialize<MessageBase>(data);
            CallListeners(message);
            message.Release();
        }

        internal void UpdateStats()
        {
            if (!_server.GetConnectionStatus(HConn, out int ping, out float loss)) return;

            _pingTracker.Record(ping);
            PacketLoss = loss;
        }

        /// <summary>Sends a typed message to this specific client.</summary>
        public void Emit<T>(int type, T data)
        {
            MessageBase message = MessageBase.Create(type, Serializer.Serialize(data));
            _server.SendToConnection(HConn, Serializer.Serialize(message));
            message.Release();
        }

        public bool Disconnect()
        {
            _server.KickSession(this);
            return true;
        }

        // IKeyDataConnection.Tick — no-op, message dispatch happens in GNSServer.Tick
        public void Tick() { }

        /// <summary>Registers a message listener for the given scope and message key.</summary>
        public void On(AuthorizationScope scope, int key, NetworkActionCaller caller)
        {
            if (!_messageListeners.ContainsKey(scope))
                _messageListeners.Add(scope, new Dictionary<int, List<NetworkActionCaller>>());

            if (!_messageListeners[scope].ContainsKey(key))
                _messageListeners[scope].Add(key, new List<NetworkActionCaller>());

            _messageListeners[scope][key].Add(caller);
        }

        /// <summary>Unregisters a previously added message listener.</summary>
        public void Off(AuthorizationScope scope, int key, NetworkActionCaller caller)
        {
            if (!_messageListeners.ContainsKey(scope)) return;
            if (!_messageListeners[scope].ContainsKey(key)) return;
            _messageListeners[scope][key].Remove(caller);
        }

        /// <summary>Bulk-registers all listeners for the given scope from a pre-built listener map.</summary>
        public void RegisterListeners(
            Dictionary<AuthorizationScope, Dictionary<int, List<NetworkActionCaller>>> listeners,
            AuthorizationScope scope)
        {
            if (!listeners.ContainsKey(scope)) return;
            foreach (var kv in listeners[scope])
                foreach (var caller in kv.Value)
                    On(scope, kv.Key, caller);
        }

        /// <summary>Bulk-unregisters all listeners for the given scope from a pre-built listener map.</summary>
        public void UnRegisterListeners(
            Dictionary<AuthorizationScope, Dictionary<int, List<NetworkActionCaller>>> listeners,
            AuthorizationScope scope)
        {
            if (!listeners.ContainsKey(scope)) return;
            foreach (var kv in listeners[scope])
                foreach (var caller in kv.Value)
                    Off(scope, kv.Key, caller);
        }

        private void CallListeners(MessageBase message)
        {
            foreach (var scopeEntry in _messageListeners)
            {
                if (!scopeEntry.Value.ContainsKey(message.type)) continue;

                object sessionId = this;
                if (_sessionIdentifierProvider.ContainsKey(scopeEntry.Key))
                    sessionId = _sessionIdentifierProvider[scopeEntry.Key](this);

                if (sessionId == null) return; // scope registered but identifier not yet resolved (e.g. not yet authenticated)

                foreach (var caller in scopeEntry.Value[message.type])
                    caller.Call(sessionId, message.data);  // byte[] → params byte[][] wraps automatically

                return; // stop at first matching scope — a message belongs to exactly one scope
            }
        }
    }
}
