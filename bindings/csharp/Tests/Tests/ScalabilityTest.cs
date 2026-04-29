using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace GameNetworkingSockets.Tests.Cases
{
    public class ScalabilityTest : ITest
    {
        public string Name => "Connection Scalability";
        public bool LongRunning => true;

        private static readonly int[] Steps = { 100, 500, 1000, 5000 };
        private const ushort PORT = 27017;
        private const string ADDR = "127.0.0.1:27017";

        public bool Run(TestContext ctx)
        {
            bool overallOk = true;

            foreach (int target in Steps)
            {
                var scaleServer  = new NetworkingServer(PORT);
                var scaleClients = new List<NetworkingClient>(target);
                int connected    = 0;
                scaleServer.OnClientConnected += _ => Interlocked.Increment(ref connected);

                // Pump thread: continuously RunCallbacks on server + all clients
                var cts      = new CancellationTokenSource();
                var pumpLock = new object();
                var pumpThread = new Thread(() =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        lock (pumpLock)
                        {
                            scaleServer.RunCallbacks();
                            foreach (var c in scaleClients)
                                c.RunCallbacks();
                        }
                        Thread.Sleep(1);
                    }
                }) { IsBackground = true };
                pumpThread.Start();

                var sw = Stopwatch.StartNew();

                for (int i = 0; i < target; i++)
                {
                    var c = new NetworkingClient();
                    if (c.Connect(ADDR))
                    {
                        lock (pumpLock) scaleClients.Add(c);
                    }
                    else
                        c.Dispose();
                }

                int attempted = scaleClients.Count;
                int timeoutMs = Math.Max(5000, target * 2);

                var deadline = Stopwatch.StartNew();
                while (Volatile.Read(ref connected) < attempted && deadline.ElapsedMilliseconds < timeoutMs)
                    Thread.Sleep(5);

                sw.Stop();
                cts.Cancel();
                pumpThread.Join();

                double rate    = attempted > 0 ? connected * 100.0 / attempted : 0;
                double connSec = sw.ElapsedMilliseconds > 0 ? connected / (sw.ElapsedMilliseconds / 1000.0) : 0;
                bool   osLimit = attempted < target;
                bool   stepOk  = connected == attempted && !osLimit;
                if (!stepOk && !osLimit) overallOk = false;

                string status = osLimit ? "!" : (stepOk ? "\u2713" : "\u2717");
                string note   =
                    osLimit ? $"  [OS limit: only {attempted}/{target} sockets created]" :
                    !stepOk ? $"  [timeout: {connected}/{attempted} connected]" : "";

                Console.WriteLine(
                    $"  {status} {target,6} clients : connected={connected}/{attempted}  " +
                    $"rate={rate:0.0}%  time={sw.ElapsedMilliseconds}ms  ({connSec:0.0} conn/s){note}");

                lock (pumpLock)
                {
                    foreach (var c in scaleClients) { try { c.Dispose(); } catch { } }
                    scaleClients.Clear();
                }
                try { scaleServer.Dispose(); } catch { }
                Thread.Sleep(200);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            return overallOk;
        }
    }
}
