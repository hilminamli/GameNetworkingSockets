# GameNetworkingSockets — Node.js / TypeScript bindings

N-API bindings for Valve's [GameNetworkingSockets](https://github.com/ValveSoftware/GameNetworkingSockets).
Provides `GnsServer` (listen) and `GnsClient` (connect) with an EventEmitter API — suitable for a
multi-server mesh where each node both listens and dials out to other nodes.

The package ships **prebuilt** native binaries for Windows x64 and Linux x64 (the self-contained GNS
library — same libs the C# binding uses — sits next to each `.node`). So **consumers need no C++
compiler**: `node-gyp-build` loads the matching prebuilt at runtime.

## Install (in your server project)

The package isn't published to npm; install it from a tarball or local path.

```bash
# from a packed tarball (recommended for deploy):
npm install ./gamenetworkingsockets-0.1.0.tgz

# or directly from the local binding folder:
npm install /path/to/GameNetworkingSockets-fork/bindings/nodejs
```

No build step runs on install — the prebuilt binary for your platform is used directly.
Supported out of the box: **win32-x64**, **linux-x64**. Other platforms need a local rebuild
(see *Building from source*) or an added prebuild.

To produce the tarball yourself, run `npm pack` in this folder (after `npm run build && npm run prebuild`
on each platform — see below).

## Building from source (contributors only)

Requires Node ≥18 and a C++ toolchain (MSVC Build Tools on Windows, gcc on Linux).

```bash
npm install        # installs deps (no native build — install script removed)
npm run build      # node-gyp rebuild + tsc
npm run prebuild   # emit prebuilds/<platform>-<arch>/ for distribution
```

Run `npm run build && npm run prebuild` once **on each target platform** (Windows, then Linux/WSL),
then `npm pack` to bundle all collected prebuilds into one tarball.

## Quick start

```ts
import { GnsServer, GnsClient, SendType, shutdown } from 'gamenetworkingsockets';

// --- Server node ---
const server = new GnsServer(27015);
server.on('connect',    (conn) => console.log('client', conn, 'joined'));
server.on('message',    (conn, data) => server.send(conn, Buffer.from('echo:' + data)));
server.on('disconnect', (conn, reason) => console.log('client', conn, 'left', reason));

// --- Connecting to another node (client role) ---
const client = new GnsClient();
client.on('connect', () => client.send(Buffer.from('hello'), SendType.Reliable));
client.on('message', (_conn, data) => console.log('got', data.toString()));
client.connect('10.0.0.5:27015');

// On shutdown:
// client.destroy(); server.destroy(); shutdown();
```

## API

### Library
- `init()` — initialize GNS (called automatically by the first peer; idempotent).
- `shutdown()` — tear down GNS. Call after destroying all peers.
- `setTickInterval(ms)` — how often the library polls callbacks + messages (default 16ms ≈ 60Hz).

### `GnsServer(port)`
- Events: `'connect'(conn)`, `'message'(conn, data, flags)`, `'disconnect'(conn, endReason, endDebug)`.
- `send(conn, data, sendType?)`, `broadcast(data, sendType?)`, `kick(conn, reason?, debug?)`.
- `clients: Set<number>` — current connection handles.
- `getStatus(conn)` → `{ ping, packetLoss }` | null.
- `destroy()`.

### `GnsClient()`
- Events: same as server.
- `connect("ip:port")` → connection handle, `send(data, sendType?)`, `disconnect(reason?, debug?)`.
- `conn: number`, `connected: boolean`.
- `getStatus(conn)`, `destroy()`.

### `SendType`
`Unreliable | NoNagle | Reliable | ReliableNoNagle`.

## How it works

- One shared `GameNetworkingSockets_Init`; all peers use the single global interface.
- GNS is single-threaded and emits one global connection-status callback. A single libuv timer
  ("tick") calls `RunCallbacks()` then drains each peer's messages — status changes are routed to
  the owning peer (by listen socket / connection handle) and surfaced as events. No background
  threads, so message/connect callbacks always fire on the JS thread.
- Servers auto-accept incoming connections and pool them (mirrors the C# binding).

## Test

```bash
npm run build && node dist/test/smoke.js   # loopback server+client round trip
```

## Layout

```
binding.gyp            node-gyp build recipe (links native/, includes deps/include)
src/addon.cpp          N-API glue over the GNS flat C API
src-ts/index.ts        GnsServer / GnsClient EventEmitter classes + types
src-ts/test/smoke.ts   loopback round-trip test
native/<platform>/     self-contained GNS library + import lib
deps/include/steam/    GNS headers
```

## Status

Built and smoke-tested (loopback round trip) on **Windows x64** and **Linux x64**, including a
compiler-free install from a packed tarball in a clean consumer project.

## Notes / not yet implemented

- **Direct IP only**, by design — `CreateListenSocketIP` / `ConnectByIPAddress`. This matches the
  C# binding, which also does not wire up P2P. For a server-to-server mesh with known addresses,
  direct IP is all you need. P2P / relay (`ConnectP2P`, Steam Datagram Relay) is only relevant for
  NAT-bound peers (e.g. player machines) and is out of scope for both bindings.
- Batched `SendMessages` not used yet — `broadcast` loops per-connection. Fine for a handful of
  peers; switch to the batched flat call if you broadcast to many clients every tick.
- macOS native lib (`native/osx-x64/`) is not bundled; add the dylib and rebuild.
- Prebuilt binaries: consider `prebuildify` to ship `.node` per platform and skip the build step.
