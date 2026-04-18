using System;
using System.Collections.Generic;
using System.Reflection;
using Valve.Sockets;
using ThrowiaServer.Network;
using ThrowiaServer.Network.Serializer;

namespace ThrowiaServer.Network.GNSNetwork
{

/// <summary>IKeyDataConnection implementation over GNS for the Unity client. Handles connect, reconnect, send/receive, and ping stats.</summary>
public class GNSConnection : IKeyDataConnection
{
    public bool IsConnected => _client.IsConnected;

    public Action OnConnected    { get; set; }
    public Action OnDisconnected { get; set; }
    public INetworkSerializer Serializer { get; }

    public int   LastPing    => _pingTracker.LastPing;
    public int   AveragePing => _pingTracker.AveragePing;
    public int   Jitter      => _pingTracker.Jitter;
    /// <summary>Packet loss ratio 0..1, where 0 = no loss. Updated once per StatsIntervalSec.</summary>
    public float PacketLoss  { get; private set; }

    /// <summary>Delay in milliseconds before attempting a reconnect after an unexpected disconnect. Set to 0 to disable.</summary>
    public int reconnectMilliseconds = 3000;

    private const float StatsIntervalSec = 1f;
    private readonly PingTracker _pingTracker = new PingTracker();
    private float _statsTimer;

    private readonly NetworkingClient _client;
    private readonly string _address;
    private readonly int    _port;

    private readonly Dictionary<int, List<MethodInfo>> _handlers = new Dictionary<int, List<MethodInfo>>();

    private readonly NetworkMessage[] _msgBuffer = new NetworkMessage[ThrowiaNetworkConstants.MessageBufferSize];

    private bool     _stopped;
    private DateTime _lastConnectAttempt = DateTime.UtcNow;

    /// <summary>Creates a GNSConnection targeting the given address and port.</summary>
    public GNSConnection(string address, int port, INetworkSerializer serializer)
    {
        _address   = address;
        _port      = port;
        Serializer = serializer;
        _client    = new NetworkingClient();

        _client.OnConnected    += () => OnConnected?.Invoke();
        _client.OnDisconnected += (reason, debug) => OnDisconnected?.Invoke();
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────────

    /// <summary>Initiates a connection to the configured server address.</summary>
    public bool ConnectToServer()
    {
        _stopped = false;
        _lastConnectAttempt = DateTime.UtcNow;
        return _client.Connect($"{_address}:{_port}");
    }

    /// <summary>Disconnects from the server and disables automatic reconnect.</summary>
    public bool DisconnectFromServer()
    {
        _stopped = true;
        _client.Disconnect();
        return true;
    }

    // ── Tick — call from Update() ─────────────────────────────────────────────

    /// <summary>Processes callbacks, updates stats, and dispatches incoming messages. Call once per Unity Update().</summary>
    public void Tick()
    {
        CheckReconnect();
        _client.RunCallbacks();

        _statsTimer += Time.deltaTime;
        if (_statsTimer >= StatsIntervalSec) { UpdateStats(); _statsTimer = 0f; }

        int count = _client.ReceiveMessages(_msgBuffer);
        for (int i = 0; i < count; i++)
        {
            var msg = Serializer.Deserialize<MessageBase>(_msgBuffer[i].data);
            try   { Dispatch(msg); msg.Release(); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
        }
    }

    // ── Send ──────────────────────────────────────────────────────────────────

    /// <summary>Sends a typed message to the server.</summary>
    public void Emit<T>(int type, T data)
    {
        MessageBase msg = MessageBase.Create(type, Serializer.Serialize(data));
        _client.SendMessage(Serializer.Serialize(msg));
        msg.Release();
    }

    // ── Listeners ─────────────────────────────────────────────────────────────

    /// <summary>Registers a message handler method for the given message key. The method must have exactly one parameter.</summary>
    public void On(int key, MethodInfo handler)
    {
        if (handler.GetParameters().Length != 1)
            throw new ArgumentException($"Handler {handler.Name} must have exactly 1 parameter.");
        if (!_handlers.ContainsKey(key))
            _handlers.Add(key, new List<MethodInfo>());
        _handlers[key].Add(handler);
    }

    /// <summary>Unregisters a previously added message handler.</summary>
    public void Off(int key, MethodInfo handler)
    {
        if (_handlers.ContainsKey(key))
            _handlers[key].Remove(handler);
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private void Dispatch(MessageBase msg)
    {
        if (!_handlers.ContainsKey(msg.type)) return;
        foreach (var method in _handlers[msg.type])
        {
            Type paramType = method.GetParameters()[0].ParameterType;
            method.Invoke(null, new[] { Serializer.Deserialize(paramType, msg.data) });
        }
    }

    private void UpdateStats()
    {
        if (!IsConnected || _client.Connection == 0) return;
        if (!_client.GetConnectionStatus(_client.Connection, out int ping, out float loss)) return;

        _pingTracker.Record(ping);
        PacketLoss = loss;
    }

    private void CheckReconnect()
    {
        // Connection != 0 means a handshake is already in progress — skip until it resolves
        if (_stopped || IsConnected || _client.Connection != 0) return;
        if ((DateTime.UtcNow - _lastConnectAttempt).TotalMilliseconds >= reconnectMilliseconds)
        {
            _lastConnectAttempt = DateTime.UtcNow;
            _client.Connect($"{_address}:{_port}");
        }
    }

}
