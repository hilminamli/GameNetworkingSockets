using System.Linq;

namespace GameNetworkingSockets.Tests.Cases
{
    // Probes whether the SteamNetConnectionStatusChangedCallback_t struct offsets used by the
    // binding match the layout produced by native GNS on the current platform. If pack(4) vs
    // pack(8) is read wrong, endReason/endDebug come through as garbage.
    //
    // Triggers a disconnect by closing the client side, then inspects the disconnect callback
    // values on the server side. Connection close on the wire produces a non-zero endReason and
    // a non-empty endDebug string in well-formed UTF-8.
    public class OffsetProbeTest : ITest
    {
        public string Name => "OffsetProbe (callback struct layout)";
        public bool LongRunning => false;

        public bool Run(TestContext ctx)
        {
            var server = ctx.Server!;
            uint clientHandle = ctx.HandleByClientIndex.TryGetValue(0, out var h) ? h : 0u;
            if (clientHandle == 0)
            {
                TestHelpers.Print(false, "no client0 handle in context — run after ConnectTest");
                return false;
            }

            int  capturedReason = 0;
            string? capturedDebug = null;
            bool captured = false;

            void Handler(uint hConn, int reason, string debug)
            {
                if (hConn != clientHandle) return;
                capturedReason = reason;
                capturedDebug  = debug;
                captured       = true;
            }

            server.OnClientDisconnected += Handler;
            try
            {
                ctx.Clients[0].Dispose();

                bool done = TestHelpers.WaitUntil(
                    () => captured,
                    timeoutMs: 5000,
                    tick: () => TestHelpers.PumpAll(server, ctx.Clients.Skip(1)));

                if (!done)
                {
                    TestHelpers.Print(false, "disconnect callback never fired");
                    return false;
                }

                System.Console.WriteLine($"    endReason = {capturedReason}");
                System.Console.WriteLine($"    endDebug  = \"{capturedDebug}\" (len={capturedDebug?.Length ?? 0})");

                // Sanity: GNS uses ESteamNetConnectionEnd codes — system close codes live in 1xxx-2xxx,
                // app codes in 1000-2999. A zero or wildly out-of-range value (negative, > 10000) means
                // we read the wrong offset.
                bool reasonSane = capturedReason > 0 && capturedReason < 10000;
                bool debugSane  = !string.IsNullOrEmpty(capturedDebug) && capturedDebug.Length < 128
                                   && capturedDebug.All(ch => ch >= ' ' && ch < 127); // printable ASCII

                bool ok = reasonSane && debugSane;
                TestHelpers.Print(ok,
                    ok ? "callback offsets look correct"
                       : $"SUSPECT: reasonSane={reasonSane} debugSane={debugSane} — pack(4)/pack(8) layout may be wrong");

                ctx.Clients.RemoveAt(0);
                ctx.HandleByClientIndex.Remove(0);
                ctx.ClientIndexByHandle.Remove(clientHandle);
                return ok;
            }
            finally
            {
                server.OnClientDisconnected -= Handler;
            }
        }
    }
}
