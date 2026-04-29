using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace GameNetworkingSockets.Tests
{
    public static class TestHelpers
    {
        public static bool WaitUntil(Func<bool> condition, int timeoutMs, Action tick)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                tick();
                if (condition()) return true;
                Thread.Sleep(1);
            }
            tick();
            return condition();
        }

        public static void PumpAll(NetworkingServer? server, IEnumerable<NetworkingClient> clients)
        {
            server?.RunCallbacks();
            foreach (var c in clients) c.RunCallbacks();
        }

        public static void PumpFor(int ms, NetworkingServer? server, IEnumerable<NetworkingClient> clients)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                PumpAll(server, clients);
                Thread.Sleep(1);
            }
        }

        public static void Drain(NetworkingServer? server, IEnumerable<NetworkingClient> clients)
        {
            PumpFor(50, server, clients);
            server?.ReceiveMessages((_, _) => { });
            foreach (var c in clients) c.ReceiveMessages((_, _) => { });
        }

        public static void Print(bool ok, string msg)
        {
            Console.WriteLine(ok ? $"  \u2713 {msg}" : $"  \u2717 {msg}");
        }
    }
}
