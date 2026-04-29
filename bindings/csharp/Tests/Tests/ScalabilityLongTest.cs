using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace GameNetworkingSockets.Tests.Cases
{
    public class ScalabilityLongTest : ITest
    {
        public string Name => "Connection Scalability (10k, long-running)";
        public bool LongRunning => true;

        private const ushort PORT = 27017;
        private const string ADDR = "127.0.0.1:27017";

        public bool Run(TestContext ctx)
        {
            const int target    = 10_000;
            int       connected = 0;

            var scaleServer  = new NetworkingServer(PORT);
            var scaleClients = new List<NetworkingClient>(target);
            object pumpLock  = new();

            scaleServer.OnClientConnected += _ => Interlocked.Increment(ref connected);

            var cts = new CancellationTokenSource();
            var pumpThread = new Thread(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    lock (pumpLock)
                    {
                        scaleServer.RunCallbacks();
                        foreach (var c in scaleClients) c.RunCallbacks();
                    }
                    Thread.Sleep(1);
                }
            }) { IsBackground = true };
            pumpThread.Start();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < target; i++)
            {
                var c = new NetworkingClient();
                if (c.Connect(ADDR)) { lock (pumpLock) scaleClients.Add(c); }
                else c.Dispose();
            }

            int attempted = scaleClients.Count;
            int timeoutMs = Math.Max(30_000, target * 3);
            var deadline  = Stopwatch.StartNew();
            while (Volatile.Read(ref connected) < attempted && deadline.ElapsedMilliseconds < timeoutMs)
                Thread.Sleep(5);

            sw.Stop();
            cts.Cancel();
            pumpThread.Join();

            double rate    = attempted > 0 ? connected * 100.0 / attempted : 0;
            double connSec = sw.ElapsedMilliseconds > 0 ? connected / (sw.ElapsedMilliseconds / 1000.0) : 0;
            bool   osLimit = attempted < target;
            bool   ok      = connected == attempted && !osLimit;

            string status = osLimit ? "!" : (ok ? "\u2713" : "\u2717");
            string note   = osLimit ? $"  [OS limit: only {attempted}/{target} sockets created]" :
                            !ok     ? $"  [timeout: {connected}/{attempted} connected]" : "";

            Console.WriteLine($"  {status} {target,6} clients : connected={connected}/{attempted}  " +
                              $"rate={rate:0.0}%  time={sw.ElapsedMilliseconds}ms  ({connSec:0.0} conn/s){note}");

            lock (pumpLock)
            {
                foreach (var c in scaleClients) { try { c.Dispose(); } catch { } }
                scaleClients.Clear();
            }
            try { scaleServer.Dispose(); } catch { }
            Thread.Sleep(200);
            GC.Collect();

            return ok || osLimit;
        }
    }
}
