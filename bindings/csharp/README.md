# GameNetworkingSockets — C# Bindings

C# bindings for [GameNetworkingSockets](../../README.md) (GNS), Valve's reliable-UDP
transport library. Ships as a single managed assembly (`GameNetworkingSockets.CSharp`,
netstandard2.1) with self-contained native libraries for **win-x64**, **linux-x64**,
**macOS (universal arm64+x86_64)**, and **iOS** (static xcframework, via the
[Unity package](../unity/README.md)).

The binding has two layers:

| Layer | Namespace | What it is |
|---|---|---|
| **Transport** | `GameNetworkingSockets.Transport` | High-level, event-driven client/server API. Start here. |
| **Interop** | `GameNetworkingSockets` | Thin hand-written P/Invoke layer over the GNS flat API (`NetworkingClient`, `NetworkingServer`, config, stats). Use when you need direct control over connection handles. |

Everything is allocation-conscious: receive paths hand you `ReadOnlySpan<byte>` views
over native memory, send paths take `ReadOnlySpan<byte>`, and server broadcast batches
into a single native call with zero managed allocation.

- [Installation](#installation)
- [Quick start](#quick-start)
- [Core concepts](#core-concepts)
- [Client usage](#client-usage)
- [Server usage](#server-usage)
- [Send types](#send-types)
- [Connection statistics](#connection-statistics)
- [Configuration](#configuration)
- [Debug logging](#debug-logging)
- [P2P with custom signaling](#p2p-with-custom-signaling)
- [Unity](#unity)
- [Dropping down to the Interop layer](#dropping-down-to-the-interop-layer)
- [Threading model](#threading-model)
- [Building the native libraries](#building-the-native-libraries)
- [Tests](#tests)

## Installation

### .NET (server, console, desktop)

The package is not published on nuget.org; pack it from this repo into a local feed:

```bash
cd bindings/csharp
dotnet pack -c Release
dotnet nuget push bin/Release/GameNetworkingSockets.CSharp.*.nupkg --source <your-local-feed>
```

then reference it:

```bash
dotnet add package GameNetworkingSockets.CSharp
```

The package embeds the native libraries under `runtimes/{win-x64,linux-x64,osx}/native/`
and ships an MSBuild `.targets` file that copies the right one next to your build output
automatically — no manual native-DLL handling. The native libraries are self-contained
(OpenSSL, protobuf, and abseil are statically merged in), so each platform needs exactly
one file: `GameNetworkingSockets.dll` / `libGameNetworkingSockets.so` /
`libGameNetworkingSockets.dylib`.

Alternatively, reference the project directly:

```xml
<ProjectReference Include="path/to/bindings/csharp/GameNetworkingSockets.csproj" />
```

### Unity

See [Unity](#unity) below.

## Quick start

A complete echo server and client. Both sides follow the same lifecycle:
**initialize the library once → create a transport → tick it every frame → dispose →
kill the library**.

**Server:**

```csharp
using GameNetworkingSockets;
using GameNetworkingSockets.Transport;

if (!NetworkingLibrary.Initialize(out string err))
    throw new Exception($"GNS init failed: {err}");

using (var server = new ServerTransport(port: 27015))
{
    server.OnConnected += conn =>
    {
        Console.WriteLine($"Client {conn.Id} connected");
        conn.OnMessage += data => conn.Send(data);   // echo back
    };
    server.OnDisconnected += conn => Console.WriteLine($"Client {conn.Id} left");

    server.Start();
    while (running)          // flip from your shutdown path
    {
        server.Tick();       // process callbacks + dispatch received messages
        Thread.Sleep(10);    // your game loop cadence
    }
}

NetworkingLibrary.Kill();
```

**Client:**

```csharp
using System.Text;
using GameNetworkingSockets;
using GameNetworkingSockets.Transport;

if (!NetworkingLibrary.Initialize(out string err))
    throw new Exception($"GNS init failed: {err}");

using (var client = new ClientTransport("127.0.0.1", 27015))
{
    client.OnConnected    += () => client.Send(Encoding.UTF8.GetBytes("hello"));
    client.OnMessage      += data => Console.WriteLine($"echo: {Encoding.UTF8.GetString(data)}");
    client.OnDisconnected += () => Console.WriteLine("disconnected");

    client.Connect();
    while (running)
    {
        client.Tick();
        Thread.Sleep(10);
    }
}

NetworkingLibrary.Kill();
```

> **Dev note:** GNS authenticates IP connections by default. For local development
> without certificates, allow unauthenticated connections after `Initialize`:
>
> ```csharp
> NetworkingLibrary.SetGlobalConfig(NetworkingConfigValue.IP_AllowWithoutAuth, 1);
> ```

## Core concepts

**Library lifetime.** `NetworkingLibrary.Initialize(out error)` must be called once per
process before creating any socket or transport; `NetworkingLibrary.Kill()` shuts the
native library down. For P2P you must use the `Initialize(string genericIdentity, out error)`
overload — see [P2P](#p2p-with-custom-signaling).

**The tick model.** Nothing is dispatched in the background. Each `Tick()` call runs
pending GNS callbacks and drains received messages, firing your event handlers *on the
calling thread*. Call it once per frame / server tick. No tick, no events — including
connection timeouts being noticed.

**Message lifetime.** `OnMessage` hands you a `ReadOnlySpan<byte>` pointing directly at
native memory. It is valid **only inside the handler** — don't store it, don't cross an
`await` with it, don't hand it to another thread. Copy out anything you need to keep:

```csharp
conn.OnMessage += data =>
{
    var copy = data.ToArray();          // only if you need it after the handler returns
    ProcessInline(data);                // fine: used synchronously inside the handler
};
```

**Messages, not streams.** Like UDP, each `Send` produces one discrete message that
arrives whole (or, if unreliable, possibly not at all) — no manual framing needed. Unlike
UDP, reliable messages may be larger than the MTU; GNS fragments, retransmits, and
reassembles them (up to `RecvMaxMessageSize`, default 512 KB).

**Result codes.** If you use the Interop layer: `EResult.OK == 1`, not 0. Always compare
against `EResult.OK`, never against zero.

## Client usage

```csharp
var client = new ClientTransport("game.example.com", 27015);
```

| Member | Notes |
|---|---|
| `Connect()` | Begins connecting; `OnConnected` fires when established. Returns `false` if the attempt could not even start. |
| `Disconnect()` | Closes the connection. |
| `Tick()` | Pump once per frame. Fires events. |
| `Send(span, sendType)` | Sends raw bytes to the server. Default `SendType.Reliable`. |
| `IsConnected` | True while in Connected state. |
| `OnConnected` / `OnDisconnected` | Connection lifecycle events. |
| `OnMessage` | Per-message event (span rules above). |
| `GetConnectionStatus(out pingMs, out packetLoss)` | Cheap ping/loss snapshot. `false` if not connected. |
| `GetConnectionStats(out ConnectionStats)` | Full real-time stats block. |

The optional `messageBufferSize` constructor parameter caps how many messages one
`Tick()` drains (default 64). Increase it if the server sends large bursts (e.g. world
snapshots) that should be consumed within a single tick.

Connection timeouts surface as `OnDisconnected` — the initial connect phase gives up
after `TimeoutInitial` (default 10 s), an established connection after `TimeoutConnected`
of silence (default 10 s). Both are [configurable](#configuration).

## Server usage

```csharp
var server = new ServerTransport(port: 27015);
```

| Member | Notes |
|---|---|
| `Start()` | Creates the listen socket. Incoming connections are accepted automatically. |
| `Stop()` | Closes all connections and the listen socket (also called by `Dispose`). |
| `Tick()` | Pump once per tick. Fires events. |
| `Broadcast(span, sendType)` | Sends to all connected clients in one batched native call — zero allocation. |
| `OnConnected` / `OnDisconnected` | Fire with the `IConnection` that joined/left. |

Each connected client is an `IConnection`:

```csharp
server.OnConnected += conn =>
{
    Console.WriteLine($"{conn.Id} joined");

    conn.OnMessage += data => HandlePacket(conn, data);
    conn.OnDisconnected += () => RemovePlayer(conn);

    conn.Send(welcomePacket);            // per-client send
    // conn.Disconnect();                // kick
    // conn.GetConnectionStatus(out int ping, out float loss);
};
```

`IConnection.Id` is a stable unique string for the lifetime of that connection —
suitable as a dictionary key for your player/session map.

## Send types

`SendType` maps directly to GNS send flags:

| Value | Guarantees | Use for |
|---|---|---|
| `Reliable` | Delivered, in order, fragmented/reassembled if large. Nagle-coalesced (5 ms) with other small sends. | State changes, chat, RPCs — the default. |
| `ReliableNoNagle` | As above, but ships immediately without coalescing. | Latency-critical reliable messages. |
| `Unreliable` | Best-effort, may drop, never blocks. Nagle-coalesced. | High-frequency snapshots superseded next tick. |
| `UnreliableNoNagle` | Best-effort, skips Nagle buffering. | Time-sensitive telemetry. |
| `UnreliableNoDelay` | As NoNagle, and also **dropped instead of buffered** if the send path can't take it right now. | Data that is worthless if even slightly late (e.g. voice). |

Nagle coalescing (default 5 ms, tunable via `NetworkingConfigValue.NagleTime`) trades a
tiny delay for fewer, fuller UDP packets. `FlushMessages` (Interop layer) force-flushes
anything Nagle is holding.

## Connection statistics

Two granularities, on both `IClientTransport` and `IConnection`:

```csharp
// Cheap: ping + local packet loss
if (client.GetConnectionStatus(out int pingMs, out float packetLoss))
    ui.SetPing(pingMs);

// Full block
if (client.GetConnectionStats(out ConnectionStats s))
    Console.WriteLine($"ping={s.PingMs}ms loss={s.PacketLossLocal:P1} " +
                      $"out={s.OutBytesPerSec}B/s queue={s.QueueTimeMicroseconds}µs");
```

`ConnectionStats` fields: `PingMs`, `PacketLossLocal`, `PacketLossRemote`,
`OutPacketsPerSec`, `OutBytesPerSec`, `InPacketsPerSec`, `InBytesPerSec`,
`SendRateBytesPerSecond` (estimated capacity), `PendingUnreliable`, `PendingReliable`
(bytes queued to send), `SentUnackedReliable`, `QueueTimeMicroseconds` (how long a new
message would wait before hitting the wire).

Polling once per second is plenty; on a busy server, round-robin a subset of connections
per tick instead of polling everyone every tick.

## Configuration

The binding imposes **no defaults of its own** — every knob reflects the GNS native
default until you override it. Set values after `Initialize`, before creating
connections (connection-scoped values propagate as defaults only to connections created
*after* the call):

```csharp
NetworkingLibrary.SetGlobalConfig(NetworkingConfigValue.SendRateMax, 1024 * 1024); // 1 MB/s
NetworkingLibrary.SetGlobalConfig(NetworkingConfigValue.TimeoutConnected, 30_000); // 30 s
NetworkingLibrary.SetGlobalConfigString(NetworkingConfigValue.P2P_STUN_ServerList,
    "stun.l.google.com:19302");
```

Most-used values (see `NetworkingConfigValue` XML docs for the full annotated list):

| Value | Default | Meaning |
|---|---|---|
| `SendBufferSize` | 512 KB | Pending-send cap; exceeding it fails the send. |
| `SendRateMin` / `SendRateMax` | 256 KB/s | Send rate clamp. Set equal to pin a constant rate. |
| `NagleTime` | 5000 µs | Small-message coalescing delay. 0 disables Nagle. |
| `RecvBufferSize` / `RecvBufferMessages` | 1 MB / 1000 | Inbound buffering caps; overflow drops packets. |
| `RecvMaxMessageSize` | 512 KB | Largest acceptable single message; senders exceeding it are disconnected. |
| `MTU_PacketSize` | 1300 | Outbound UDP payload size. |
| `TimeoutInitial` / `TimeoutConnected` | 10 s | Connect-phase / idle timeouts (ms). |
| `IP_AllowWithoutAuth` | 0 | 1–2 allows unauthenticated IP connections (dev only). |
| `P2P_Transport_ICE_Enable` | disabled | ICE candidate bitmask — see [P2P](#p2p-with-custom-signaling). |

## Debug logging

GNS emits internal diagnostics on its worker threads, where you must not touch engine
APIs or do I/O. `BufferedDebugLog` is the thread-safe bridge: native enqueues, you drain
on your main thread:

```csharp
var log = new BufferedDebugLog();                                  // bounded; can't leak
NetworkingLibrary.SetDebugOutput(DebugOutputType.Important, log);  // or Verbose while debugging

// in your tick loop:
log.Drain((level, msg) => Console.WriteLine($"[GNS:{level}] {msg}"));
```

If you stop draining, the queue caps at `MaxDepth` (default 10 000) and drops oldest
messages, counting them in `DroppedCount`.

## P2P with custom signaling

Peers connect **by identity** instead of IP, with NAT traversal via ICE. You provide the
signaling side channel (typically your lobby server) that shuttles opaque rendezvous
blobs between peers; once negotiated, game traffic flows directly peer-to-peer.

Requirements:

1. A native build with `ENABLE_ICE` (+ WebRTC for real NAT traversal — the libraries in
   this repo's package include the native ICE client with STUN+TURN).
2. Initialize with an **identity** — a unique label peers use to address each other
   (max 31 chars, e.g. a lobby-assigned player id):
   ```csharp
   NetworkingLibrary.Initialize("player-42", out string err);
   ```
3. Enable ICE explicitly (the open-source build defaults to disabled) and configure STUN
   for connections across NATs:
   ```csharp
   NetworkingLibrary.SetGlobalConfig(NetworkingConfigValue.P2P_Transport_ICE_Enable, IceEnable.All);
   NetworkingLibrary.SetGlobalConfigString(NetworkingConfigValue.P2P_STUN_ServerList,
       "stun.l.google.com:19302");
   ```
4. Implement `ISignalingChannel` over your lobby connection:
   ```csharp
   public interface ISignalingChannel
   {
       // Deliver blob to the peer with that identity. May be called from GNS's
       // service thread — must be thread-safe.
       void Send(string toIdentity, byte[] blob);

       // Raise when a blob arrives from a remote peer. Any thread is fine;
       // the transport queues it and processes on its own Tick.
       event Action<byte[]> BlobReceived;
   }
   ```

Then the transports work exactly like the IP versions, created through the `P2P`
factories:

```csharp
// Host ("player-1"):
using var host = ServerTransport.P2P(channel, localVirtualPort: 0);
host.OnConnected += conn => conn.OnMessage += data => HandlePacket(conn, data);
host.Start();

// Joiner ("player-42") — connects to the host by identity, no IP anywhere:
using var client = ClientTransport.P2P(channel, "player-1", remoteVirtualPort: 0);
client.OnConnected += () => client.Send(helloPacket);
client.Connect();

// Both sides: Tick() as usual. Signaling blobs are drained automatically each Tick.
```

Virtual ports are just a rendezvous label — the joiner's `remoteVirtualPort` must match
the host's `localVirtualPort`. A runnable end-to-end example (two processes, signaling
over local TCP, ICE-negotiated connection, ping/pong verification) lives in
[`Tests.P2P/Program.cs`](Tests.P2P/Program.cs), including a minimal `ISignalingChannel`
implementation. See also [README_P2P.md](../../README_P2P.md) for the native-side
concepts.

## Unity

The binding is engine-agnostic (no UnityEngine reference) and IL2CPP/AOT-safe: every
delegate passed to native code is a static method marked `[MonoPInvokeCallback]`.

Setup:

1. **Managed DLL** — build `GameNetworkingSockets.csproj` and place
   `GameNetworkingSockets.CSharp.dll` under `Assets/` (e.g. `Assets/Plugins/`).
2. **Native plugin** — place the platform library (`GameNetworkingSockets.dll`,
   `libGameNetworkingSockets.so`, `libGameNetworkingSockets.dylib`) under
   `Assets/Plugins/<platform>/` with matching platform import settings.
3. **Editor glue** — install the [`com.throwia.gns-unity`](../unity/README.md) package.
   It kills the native library before every domain reload; without it, exiting play mode
   with live GNS state corrupts the managed heap and crashes the editor later.
4. **iOS** — iOS forbids dynamic libraries from P/Invoke; the Unity package ships a
   static `GameNetworkingSockets.xcframework` plus an iOS-only variant of the managed
   DLL that P/Invokes `__Internal`. Both are platform-restricted to iOS so they coexist
   with the desktop DLLs.

Game-side rules:

- Set `Application.runInBackground = true` — an unfocused player stops ticking, and GNS
  keepalive will kill your connections.
- Drive `Tick()` from `Update()` (or your netcode loop).
- On quit / exiting play mode, disconnect gracefully and pump a few extra ticks so the
  close handshake reaches the wire.

## Dropping down to the Interop layer

When the Transport layer's model doesn't fit (custom poll groups, per-connection user
data, connection handles in your own data structures), use the Interop classes directly.
They are the same code the transports are built on:

```csharp
var client = new NetworkingClient();
client.OnConnected    += () => Console.WriteLine("up");
client.OnDisconnected += (endReason, endDebug) => Console.WriteLine($"down: {endReason} {endDebug}");

client.Connect("203.0.113.10:27015");
while (running)
{
    client.RunCallbacks();                      // fire status events
    client.ReceiveMessages((hConn, data) => Handle(data));
    EResult r = client.SendMessage(payload, SendType.Unreliable);
    if (r != EResult.OK) HandleSendFailure(r);  // remember: OK == 1
}
client.Dispose();
```

`NetworkingServer` additionally exposes `Clients` (live connection handles),
`SendMessage(hConn, …)`, `KickClient(hConn, reason, debug)`, batched `Broadcast`, and
automatic accept/poll-group management. The common base `NetworkingSockets` exposes
`FlushMessages`, `CloseConnection`, `SetConnectionUserData`, poll-group management, and
the stats getters, all keyed by connection handle. Disconnect reasons: `endReason` is
your application-defined code when the peer called `Disconnect(reason, debug)`, or a GNS
internal code; `endDebug` is a human-readable explanation populated on peer-close and
local-problem disconnects.

## Threading model

- **Not thread-safe by design.** Create, tick, send, and dispose a transport from one
  thread. All events fire synchronously inside `Tick()` (Interop layer:
  `RunCallbacks`/`ReceiveMessages`) on the calling thread.
- Multiple transports in one process are fine (the global callback dispatcher routes by
  connection ownership) — but tick each from a single thread.
- Exactly two paths can touch other threads, both already handled for you:
  GNS debug output (bridged via `BufferedDebugLog`) and outbound signaling blobs
  (`ISignalingChannel.Send` may be called from GNS's service thread — implement it
  thread-safe).

## Building the native libraries

Prebuilt self-contained libraries are checked in under [`native/`](native/) and embedded
in the NuGet package. To rebuild from source — including the exact CMake/vcpkg flags,
the static OpenSSL/protobuf/abseil merging, and known pitfalls — see
[BUILDING_SELF_CONTAINED.md](../../BUILDING_SELF_CONTAINED.md) (Windows/Linux) and
[BUILDING_IOS.md](../../BUILDING_IOS.md) (iOS xcframework).

## Tests

```bash
cd bindings/csharp/Tests
dotnet run -c Release              # full suite: connect, send/receive, reliability,
                                   # large messages, stats, scalability, stress, leaks

cd ../Tests.P2P
dotnet run -c Release              # end-to-end P2P/ICE: spawns host+client processes,
                                   # signaling over local TCP, verifies ping/pong
```

The tests double as runnable usage examples — [`Tests/Tests/`](Tests/Tests/) covers the
IP transport surface, [`Tests.P2P/Program.cs`](Tests.P2P/Program.cs) the P2P surface.
