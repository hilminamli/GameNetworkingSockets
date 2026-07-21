// Unity editor-lifecycle glue for GameNetworkingSockets.
//
// The native library is process-lifetime; the Unity editor's managed domain is not. Every
// play-mode transition and script recompile reloads the domain, and any delegate GNS still
// holds (global status callback, custom-signaling callbacks, debug output) then points into
// freed managed memory. The next native worker-thread callback corrupts the heap or crashes
// the editor outright — often not at the reload itself but minutes later, in an innocent
// looking managed stack (Dictionary.TryInsert under RunCallbacks is the classic signature).
//
// The fix: kill the native library BEFORE every domain
// reload. Worker threads stop, every native→managed pointer is unregistered, and the next
// session re-initializes from scratch (statics reset with the domain, so IsInitialized is
// false again and consumers just call Initialize as usual).
//
// This file ships with the bindings as a Unity package so every Unity consumer gets the
// teardown automatically — no per-project boilerplate to forget.

#if UNITY_EDITOR
using UnityEditor;

namespace GameNetworkingSockets.Unity
{
    internal static class GnsUnityLifecycle
    {
        [InitializeOnLoadMethod]
        private static void RegisterDomainReloadCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload += KillBeforeDomainReload;
        }

        private static void KillBeforeDomainReload()
        {
            if (!NetworkingLibrary.IsInitialized) return;
            try { NetworkingLibrary.Kill(); }
            catch { /* teardown must never block the reload */ }
        }
    }
}
#endif
