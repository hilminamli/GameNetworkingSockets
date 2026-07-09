# GameNetworkingSockets Unity Glue

Editor-lifecycle package for the C# bindings. One job: **kill the native library before
every managed domain reload** (`AssemblyReloadEvents.beforeAssemblyReload`), so GNS worker
threads never call back into a torn-down domain. Without this, exiting play mode or
recompiling scripts while any GNS state is alive corrupts the managed heap — typically
crashing the editor later, in an unrelated-looking stack under `RunCallbacks`.

## Install

Add to your project's `Packages/manifest.json`:

```json
"com.throwia.gns-unity": "file:<path-to-fork>/bindings/unity"
```

The managed `GameNetworkingSockets.CSharp.dll` and the native library still go into
`Assets/Plugins` yourself (or via your own packaging) — this package only ships the glue
and deliberately has no hard reference to them.

## Game-side rules that still apply

- Set `Application.runInBackground = true` — an unfocused editor/player stops ticking and
  GNS keepalive kills your connections.
- On `ExitingPlayMode` / `Application.quitting`, disconnect gracefully and pump a few
  ticks so FINs reach the wire; the domain-reload kill covers the native release, not
  connection etiquette.
- In the editor, avoid Disposing GNS-backed objects from `OnDestroy` during play-mode
  exit (native `CloseConnection` races worker teardown); drop references and let the
  reload kill handle the native side.
