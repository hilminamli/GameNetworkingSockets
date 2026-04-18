using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ThrowiaServer.Network;
using ThrowiaServer.Network.Serializer;
using Valve.Sockets;

namespace ThrowiaServer.Network.GNSNetwork
{
    /// <summary>IKeyDataServer implementation over GNS. Manages sessions, message dispatch, and per-session stats updates.</summary>
    public class GNSServer : IKeyDataServer
    {
        public Dictionary<AuthorizationScope, Dictionary<int, List<NetworkActionCaller>>> MessageListeners { get; private set; }
            = new Dictionary<AuthorizationScope, Dictionary<int, List<NetworkActionCaller>>>();

        public event Action<IKeyDataSession>? OnSessionConnected;
        public event Action<IKeyDataSession>? OnSessionDisconnected;

        private readonly INetworkSerializer _serializer;
        private readonly ushort _port;

        private NetworkingServer? _gnsServer;
        private readonly NetworkMessage[] _msgBuffer = new NetworkMessage[ThrowiaNetworkConstants.MessageBufferSize];
        private readonly Queue<uint> _statsQueue = new Queue<uint>();

        private readonly Dictionary<uint, GNSSession> _sessions = new Dictionary<uint, GNSSession>();
        private readonly Dictionary<Guid, uint> _guidToConn = new Dictionary<Guid, uint>();

        private readonly Dictionary<AuthorizationScope, Func<IKeyDataSession, object>> _sessionIdentifierProvider
            = new Dictionary<AuthorizationScope, Func<IKeyDataSession, object>>();

        /// <summary>Creates a GNSServer that will listen on the given port using the provided serializer.</summary>
        public GNSServer(ushort port, INetworkSerializer serializer)
        {
            _port = port;
            _serializer = serializer;
        }

        // ── IKeyDataServer ────────────────────────────────────────────────────────

        /// <summary>Starts the server, creates the listen socket, and registers client connect/disconnect handlers.</summary>
        public bool Start()
        {
            _gnsServer = new NetworkingServer(_port);

            _gnsServer.OnClientConnected += hConn =>
            {
                var session = new GNSSession(hConn, this, _serializer, _sessionIdentifierProvider);
                session.IsConnected = true;
                _sessions[hConn] = session;
                _guidToConn[session.SessionID] = hConn;

                session.RegisterListeners(MessageListeners, AuthorizationScope.None);
                _statsQueue.Enqueue(hConn);

                session.OnConnected?.Invoke();
                OnSessionConnected?.Invoke(session);
            };

            _gnsServer.OnClientDisconnected += (hConn, reason, debug) =>
            {
                if (!_sessions.TryGetValue(hConn, out var session)) return;
                session.IsConnected = false;
                _sessions.Remove(hConn);
                _guidToConn.Remove(session.SessionID);

                session.OnDisconnected?.Invoke();
                OnSessionDisconnected?.Invoke(session);
            };

            return true;
        }

        /// <summary>Stops the server, disposes the socket, and clears all active sessions.</summary>
        public bool Stop()
        {
            _gnsServer?.Dispose();
            _gnsServer = null;
            _sessions.Clear();
            _guidToConn.Clear();
            return true;
        }

        /// <summary>Processes GNS callbacks, updates a batch of session stats, and dispatches all pending messages. Call once per server tick.</summary>
        public void Tick()
        {
            if (_gnsServer == null) return;

            _gnsServer.RunCallbacks();

            // Update stats for a fraction of sessions per tick (round-robin) to spread CPU cost evenly
            int statsBatch = Math.Max(1, _statsQueue.Count / ThrowiaNetworkConstants.StatsBatchDivisor);
            for (int i = 0; i < statsBatch && _statsQueue.Count > 0; i++)
            {
                uint hConn = _statsQueue.Dequeue();
                if (_sessions.TryGetValue(hConn, out var s))
                {
                    s.UpdateStats();
                    _statsQueue.Enqueue(hConn);
                }
            }

            int count = _gnsServer.ReceiveMessages(_msgBuffer);
            for (int i = 0; i < count; i++)
            {
                if (_sessions.TryGetValue(_msgBuffer[i].connection, out var session))
                    session.HandleMessage(_msgBuffer[i].data);
            }
        }

        /// <summary>Broadcasts a typed message to all connected sessions.</summary>
        public void Emit<T>(int type, T data)
        {
            MessageBase message = MessageBase.Create(type, _serializer.Serialize(data));
            byte[] bytes = _serializer.Serialize(message);
            _gnsServer?.Broadcast(bytes);
            message.Release();
        }

        public bool ContainsSession(Guid sessionId)
            => _guidToConn.ContainsKey(sessionId);

        public void AddSessionIdentifer(AuthorizationScope scope, Func<IKeyDataSession, object> identifier)
            => _sessionIdentifierProvider.TryAdd(scope, identifier);

        public void RemoveSessionIdentifer(AuthorizationScope scope)
            => _sessionIdentifierProvider.Remove(scope);

        // ── Listener collection ───────────────────────────────────────────────────

        /// <summary>Scans all loaded assemblies for static methods marked with the given attribute and registers them as message listeners.</summary>
        public void CollectListeners(Type attributeType)
        {
            MessageListeners = new Dictionary<AuthorizationScope, Dictionary<int, List<NetworkActionCaller>>>();

            var methods = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .Where(m => m.IsStatic && m.GetCustomAttributes(attributeType, false).Length > 0);

            foreach (var method in methods)
                RegisterMethod(method, null, attributeType);
        }

        /// <summary>Scans the given object instance for methods marked with the given attribute and registers them as message listeners.</summary>
        public void CollectListeners(object instance, Type attributeType)
        {
            var methods = instance.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.GetCustomAttributes(attributeType, false).Length > 0);

            foreach (var method in methods)
                RegisterMethod(method, instance, attributeType);
        }

        private void RegisterMethod(MethodInfo method, object? instance, Type attributeType)
        {
            ParameterInfo[] @params = method.GetParameters();
            if (@params.Length < 2)
            {
                Console.WriteLine($"[GNSServer] Skipping {method.Name}: needs at least 2 parameters.");
                return;
            }

            var attr = (NetworkListenerAttribute)(object)method.GetCustomAttribute(typeof(NetworkListenerAttribute), true)!;
            var caller = new NetworkActionCaller(_serializer, DelegateHelper.CreateActionDelegate<object[]>(method, instance),
                @params.Select(p => p.ParameterType).ToArray());

            if (!MessageListeners.ContainsKey(attr.Scope))
                MessageListeners.Add(attr.Scope, new Dictionary<int, List<NetworkActionCaller>>());
            if (!MessageListeners[attr.Scope].ContainsKey(attr.Key))
                MessageListeners[attr.Scope].Add(attr.Key, new List<NetworkActionCaller>());

            MessageListeners[attr.Scope][attr.Key].Add(caller);
        }

        // ── Internal helpers used by GNSSession ───────────────────────────────────

        internal void SendToConnection(uint hConn, byte[] data)
            => _gnsServer?.SendMessage(hConn, data);

        internal void KickSession(GNSSession session)
            => _gnsServer?.KickClient(session.HConn);

        internal bool GetConnectionStatus(uint hConn, out int pingMs, out float packetLoss)
        {
            if (_gnsServer != null)
                return _gnsServer.GetConnectionStatus(hConn, out pingMs, out packetLoss);
            pingMs = 0; packetLoss = 0f; return false;
        }
    }
}
