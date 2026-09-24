# Building GameNetworkingSockets for iOS

Produces a **static** `GameNetworkingSockets.xcframework` (device arm64 +
simulator arm64) for a Unity iOS IL2CPP build. iOS forbids loading dynamic
native libraries from the app bundle, so — unlike the desktop `.dylib` — the
transport must be linked statically into the player at build time.

Built by `.github/workflows/ios-static.yml` on the self-hosted M2 runner (the
same one `macos-dylib.yml` uses). **Manual trigger only.**

## Phasing

- **Phase 1 (default): ICE OFF, IP-only.** Goal = clear the "unable to load
  libGameNetworkingSockets" error and prove the base transport links and runs
  on-device. No P2P/NAT yet.
- **Phase 2: ICE ON.** Trigger the workflow with `enable_ice = true`. The hard
  part is building webrtc-lite for `arm64-ios` — expect the same class of fight
  as the Windows ICE build. Do this only after Phase 1 works end-to-end.

## Runner prerequisites (one-time)

- **Full Xcode** (not just Command Line Tools — the iphoneos + iphonesimulator
  SDKs are required). Select it: `sudo xcode-select -s /Applications/Xcode.app`
- `brew install cmake ninja`
- `~/vcpkg-gns` (shared with `macos-dylib.yml`; the workflow bootstraps it)

## Triplet caveat

The workflow checks `~/vcpkg-gns/triplets/community/` for `arm64-ios.cmake` and
`arm64-ios-simulator.cmake`. If the simulator triplet is missing at the pinned
`builtin-baseline` (vcpkg.json), it builds the **device slice only** (a warning
is logged) — the xcframework then runs on real devices but not the Simulator.
If even `arm64-ios` is missing, the run fails with a clear message: bump
`builtin-baseline` in `vcpkg.json` to a newer commit that ships the community
iOS triplets, then re-run.

## Using it from Unity

The Unity package (`bindings/unity`) already ships both iOS pieces under
`Runtime/Plugins/iOS/`, each import-restricted to the iOS platform:

1. **`GameNetworkingSockets.xcframework`** — the static library this workflow
   produces. Unity links it into the generated Xcode project. To update it,
   download the `GameNetworkingSockets-ios-xcframework` artifact and replace the
   folder.

2. **`GameNetworkingSockets.CSharp.dll`** — an iOS-only build of the C# wrapper.
   On desktop the wrapper loads the dynamic lib by name; on iOS (IL2CPP, static)
   P/Invoke must bind to `__Internal`, so this variant is compiled with
   `UNITY_IOS` defined. The desktop wrapper DLL must be excluded on iOS so exactly
   one wrapper is active per platform (see `bindings/csharp/README.md`).

## Rebuilding the iOS wrapper DLL

Same sources as the desktop DLL, built with `GnsIos=true` (adds `UNITY_IOS`):

```sh
dotnet build bindings/csharp/GameNetworkingSockets.csproj -c Release \
    -p:GnsIos=true -o build-ios-wrapper
cp build-ios-wrapper/GameNetworkingSockets.CSharp.dll bindings/unity/Runtime/Plugins/iOS/
```

Release builds are deterministic and map local source paths in the embedded PDB
to `/_/` (`ContinuousIntegrationBuild` in the csproj), so no machine paths end up
in the shipped DLL.
