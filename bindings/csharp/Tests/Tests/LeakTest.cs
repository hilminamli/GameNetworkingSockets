using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace GameNetworkingSockets.Tests.Cases
{
    public class LeakTest : ITest
    {
        public string Name => "Leak Soak (30 minutes)";
        public bool LongRunning => true;

        private const int DURATION_MIN = 30;
        private const int SAMPLE_INTERVAL_MS = 60_000;
        private const int PAIRS_PER_ROUND = 5;
        private const ushort PORT = 28000;
        private const string ADDR = "127.0.0.1:28000";

        public bool Run(TestContext ctx)
        {
            var proc = Process.GetCurrentProcess();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            proc.Refresh();

            long memBase = GC.GetTotalMemory(true);
            int handlesBase = proc.HandleCount;

            var memSamples = new List<double>();
            var handleSamples = new List<int>();
            var minuteTicks = new List<double>();

            memSamples.Add(memBase / (1024.0 * 1024.0));
            handleSamples.Add(handlesBase);
            minuteTicks.Add(0);

            Console.WriteLine($"  baseline: mem={memSamples[0]:0.00}MB  handles={handlesBase}");

            var overall = Stopwatch.StartNew();
            long nextSample = SAMPLE_INTERVAL_MS;
            long endAt = DURATION_MIN * 60_000L;

            while (overall.ElapsedMilliseconds < endAt)
            {
                RunRound();

                if (overall.ElapsedMilliseconds >= nextSample)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    proc.Refresh();

                    long mem = GC.GetTotalMemory(true);
                    int handles = proc.HandleCount;
                    double minute = overall.ElapsedMilliseconds / 60_000.0;

                    memSamples.Add(mem / (1024.0 * 1024.0));
                    handleSamples.Add(handles);
                    minuteTicks.Add(minute);

                    double memDelta = memSamples[^1] - memSamples[0];
                    int handleDelta = handleSamples[^1] - handleSamples[0];
                    Console.WriteLine(
                        $"  t={minute:0.0}min  mem={memSamples[^1]:0.00}MB (\u0394={memDelta:+0.00;-0.00}MB)  " +
                        $"handles={handles} (\u0394={handleDelta:+0;-0})");

                    nextSample += SAMPLE_INTERVAL_MS;
                }

                Thread.Sleep(100);
            }

            double memGrowthMB = memSamples[^1] - memSamples[0];
            int handleGrowth = handleSamples[^1] - handleSamples[0];
            double durationMin = overall.ElapsedMilliseconds / 60_000.0;
            double memPerMin = durationMin > 0 ? memGrowthMB / durationMin : 0;
            double handlesPerMin = durationMin > 0 ? handleGrowth / durationMin : 0;

            Console.WriteLine($"  Duration: {durationMin:0.0} min");
            Console.WriteLine($"  Memory growth: {memGrowthMB:+0.00;-0.00} MB total  ({memPerMin:+0.000;-0.000} MB/min)");
            Console.WriteLine($"  Handle growth: {handleGrowth:+0;-0} total  ({handlesPerMin:+0.00;-0.00}/min)");

            bool memOk = memGrowthMB <= 10.0;
            bool handleOk = handleGrowth <= 100;
            bool ok = memOk && handleOk;

            TestHelpers.Print(memOk, $"memory growth {memGrowthMB:+0.00;-0.00}MB (threshold <=10MB)");
            TestHelpers.Print(handleOk, $"handle growth {handleGrowth:+0;-0} (threshold <=100)");
            return ok;
        }

        private static void RunRound()
        {
            var server = new NetworkingServer(PORT);
            var clients = new List<NetworkingClient>(PAIRS_PER_ROUND);

            for (int i = 0; i < PAIRS_PER_ROUND; i++)
            {
                var c = new NetworkingClient();
                c.Connect(ADDR);
                clients.Add(c);
            }

            TestHelpers.WaitUntil(
                () => clients.All(c => c.IsConnected),
                timeoutMs: 3000,
                tick: () => TestHelpers.PumpAll(server, clients));

            foreach (var c in clients)
                c.SendMessage(new byte[64], SendType.Reliable);

            TestHelpers.PumpFor(100, server, clients);

            foreach (var c in clients)
            {
                try { c.Dispose(); } catch { }
            }
            try { server.Dispose(); } catch { }
            TestHelpers.PumpFor(50, null, System.Linq.Enumerable.Empty<NetworkingClient>());
        }
    }
}
