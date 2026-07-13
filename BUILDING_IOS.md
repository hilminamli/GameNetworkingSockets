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

## After the artifact: wiring it into Unity

Download `GameNetworkingSockets-ios-xcframework` and drop it into the game
project (planet-game) — this is NOT automatic. Two things beyond the binary:

1. **The C# P/Invoke must resolve on iOS.** On desktop the wrapper loads the
   dynamic lib by name; on iOS (IL2CPP, static) P/Invoke uses `__Internal`.
   The C# wrapper's `DllImport("GameNetworkingSockets")` must become
   `DllImport("__Internal")` under `UNITY_IOS`, or the entry points won't bind.
   (This lives in the C# bindings / gns-unity package, handled game-side.)

2. **Unity plugin import settings + Info.plist.** Set the xcframework's
   PluginImporter for iOS only; Unity links it into the generated Xcode project.
   Voice capture additionally needs `NSMicrophoneUsageDescription` in the
   iOS Info.plist (a Unity `PostProcessBuild` step), or the app crashes the
   moment it touches the microphone.

See planet-game `docs/voice-system.md` and the gns-unity package for the
game-side wiring.
