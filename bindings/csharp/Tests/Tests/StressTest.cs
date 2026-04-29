using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace GameNetworkingSockets.Tests.Cases
{
    public class StressTest : ITest
    {
        public string Name => "Stress Test (1000 clients, 10s)";
        public bool LongRunning => false;

        private const int    STRESS_CLIENTS    = 1000;
        private const int    CLIENTS_PER_THREAD = 200;
        private const int    DURATION_MS        = 10_000;
        private const int    SEND_INTERVAL_MS   = 50;   // each client sends every 50ms → ~20 msg/s per client
        private const ushort PORT               = 27016;
        private const string ADDR               = "127.0.0.1:27016";
        private const byte   ECHO_TAG           = 0xEC;

        public bool Run(TestContext ctx)
        {
            // ── Setup ────────────────────────────────────────────────────────────
            var stressServer  = new NetworkingServer(PORT);
            var stressClients = new List<NetworkingClient>(STRESS_CLIENTS);
            var stressHandles = new List<uint>();
            object handlesGate = new();

            stressServer.OnClientConnected += h => { lock (handlesGate) stressHandles.Add(h); };

            for (int i = 0; i < STRESS_CLIENTS; i++)
            {
                var c = new NetworkingClient();
                if (c.Connect(ADDR)) stressClients.Add(c);
            }

            Console.WriteLine($"  Connecting {stressClients.Count} clients...");
            bool allUp = TestHelpers.WaitUntil(
                () => stressClients.All(c => c.IsConnected) && stressServer.Clients.Count == stressClients.Count,
                timeoutMs: 60_000,
                tick: () => TestHelpers.PumpAll(stressServer, stressClients));

            if (!allUp)
            {
                Console.WriteLine($"  \u2717 Only {stressServer.Clients.Count}/{stressClients.Count} connected — aborting");
                foreach (var c in stressClients) c.Dispose();
                stressServer.Dispose();
                return false;
            }
            Console.WriteLine($"  All {stressClients.Count} clients connected. Running {DURATION_MS / 1000}s...");

            int clientThreadCount = (stressClients.Count + CLIENTS_PER_THREAD - 1) / CLIENTS_PER_THREAD;

            // ── Counters ─────────────────────────────────────────────────────────
            long totalSent     = 0;
            long totalReceived = 0;
            long totalBytes    = 0;

            // Echo RTT (client 0 ↔ server)
            var  echoRtts = new List<long>();
            object echoGate = new();

            // Per-second snapshots for progress line
            var snapshots = new List<(double elapsedSec, long sent, long recv)>();
            object snapGate = new();

            // Aggregate connection stats sampled every 500ms
            var statPings       = new List<int>();
            var statLossLocal   = new List<float>();
            var statOutBytes    = new List<float>();
            var statInBytes     = new List<float>();
            object statGate     = new();

            // Payload sizes: small / medium / large
            var payloads = new byte[][] { new byte[64], new byte[512], new byte[2048] };
            new Random(1).NextBytes(payloads[0]);
            new Random(2).NextBytes(payloads[1]);
            new Random(3).NextBytes(payloads[2]);
            var sendTypes = new[] { SendType.Reliable, SendType.ReliableNoNagle };

            using var cts   = new CancellationTokenSource();
            var       token = cts.Token;

            // ── Server thread ─────────────────────────────────────────────────────
            var serverThread = new Thread(() =>
            {
                var sw          = Stopwatch.StartNew();
                long lastStat   = 0;
                long lastSnap   = 0;
                long lastSentSnap = 0;
                long lastRecvSnap = 0;

                while (!token.IsCancellationRequested)
                {
                    stressServer.RunCallbacks();
                    stressServer.ReceiveMessages((hConn, data) =>
                    {
                        // Echo everything back to sender (including echo-tag messages)
                        stressServer.SendMessage(hConn, data.ToArray(), SendType.Reliable);
                        Interlocked.Increment(ref totalReceived);
                    });

                    long now = sw.ElapsedMilliseconds;

                    // Sample connection stats from up to 10 random handles every 500ms
                    if (now - lastStat >= 500)
                    {
                        lastStat = now;
                        List<uint> handles;
                        lock (handlesGate) handles = stressHandles.ToList();
                        var rng = new Random();
                        foreach (var h in handles.OrderBy(_ => rng.Next()).Take(10))
                        {
                            if (stressServer.GetConnectionStats(h, out var cs) &&
                                cs.PingMs >= 0 && cs.PacketLossLocal is >= 0f and <= 1f)
                            {
                                lock (statGate)
                                {
                                    statPings.Add(cs.PingMs);
                                    statLossLocal.Add(cs.PacketLossLocal);
                                    statOutBytes.Add(cs.OutBytesPerSec);
                                    statInBytes.Add(cs.InBytesPerSec);
                                }
                            }
                        }
                    }

                    // Per-second snapshot
                    if (now - lastSnap >= 1000)
                    {
                        lastSnap = now;
                        long s = Interlocked.Read(ref totalSent);
                        long r = Interlocked.Read(ref totalReceived);
                        lock (snapGate)
                            snapshots.Add((now / 1000.0, s - lastSentSnap, r - lastRecvSnap));
                        lastSentSnap = s;
                        lastRecvSnap = r;
                    }

                    Thread.Sleep(1);
                }
            }) { IsBackground = true, Name = "stress-server" };

            // ── Client threads (each manages CLIENTS_PER_THREAD clients) ─────────
            var clientThreads = new Thread[clientThreadCount];
            for (int t = 0; t < clientThreadCount; t++)
            {
                int start       = t * CLIENTS_PER_THREAD;
                int end         = Math.Min(start + CLIENTS_PER_THREAD, stressClients.Count);
                int threadIndex = t;

                clientThreads[t] = new Thread(() =>
                {
                    var rng      = new Random(threadIndex);
                    var sw       = Stopwatch.StartNew();
                    long nextSend = 0;

                    while (!token.IsCancellationRequested)
                    {
                        // RunCallbacks + receive for every client in this slice
                        for (int i = start; i < end; i++)
                        {
                            var c = stressClients[i];
                            c.RunCallbacks();

                            if (threadIndex == 0 && i == 0)
                            {
                                c.ReceiveMessages((_, data) =>
                                {
                                    if (data.Length == 9 && data[0] == ECHO_TAG)
                                    {
                                        long sentTick = BitConverter.ToInt64(data.Slice(1));
                                        long rttUs = (Stopwatch.GetTimestamp() - sentTick) * 1_000_000 / Stopwatch.Frequency;
                                        lock (echoGate) echoRtts.Add(rttUs);
                                    }
                                    else Interlocked.Increment(ref totalReceived);
                                });
                            }
                            else
                            {
                                c.ReceiveMessages((_, _) => Interlocked.Increment(ref totalReceived));
                            }
                        }

                        // Each client sends every SEND_INTERVAL_MS
                        long now = sw.ElapsedMilliseconds;
                        if (now >= nextSend)
                        {
                            nextSend = now + SEND_INTERVAL_MS;
                            for (int i = start; i < end; i++)
                            {
                                var payload  = payloads[rng.Next(payloads.Length)];
                                var sendType = sendTypes[rng.Next(sendTypes.Length)];
                                if (stressClients[i].SendMessage(payload, sendType) == EResult.OK)
                                {
                                    Interlocked.Increment(ref totalSent);
                                    Interlocked.Add(ref totalBytes, payload.Length);
                                }
                            }

                            // Echo ping from client 0 every 100ms
                            if (threadIndex == 0 && now % 100 < SEND_INTERVAL_MS)
                            {
                                var echoBuf = new byte[9];
                                echoBuf[0] = ECHO_TAG;
                                BitConverter.TryWriteBytes(echoBuf.AsSpan(1), Stopwatch.GetTimestamp());
                                stressClients[0].SendMessage(echoBuf, SendType.Reliable);
                            }
                        }

                        Thread.Sleep(1);
                    }
                }) { IsBackground = true, Name = $"stress-client-{t}" };
            }

            // ── Server broadcast thread ───────────────────────────────────────────
            var broadcastThread = new Thread(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    stressServer.Broadcast(payloads[0], SendType.Reliable);
                    Interlocked.Add(ref totalSent, stressClients.Count);
                    Thread.Sleep(100);
                }
            }) { IsBackground = true, Name = "stress-broadcast" };

            // ── Run ───────────────────────────────────────────────────────────────
            serverThread.Start();
            foreach (var t in clientThreads) t.Start();
            broadcastThread.Start();

            var overallSw = Stopwatch.StartNew();
            Thread.Sleep(DURATION_MS);

            cts.Cancel();
            broadcastThread.Join();
            senderJoin(clientThreads);
            serverThread.Join();
            overallSw.Stop();

            // ── Teardown ──────────────────────────────────────────────────────────
            foreach (var c in stressClients) { try { c.Dispose(); } catch { } }
            try { stressServer.Dispose(); } catch { }

            // ── Results ───────────────────────────────────────────────────────────
            double elapsedSec = overallSw.ElapsedMilliseconds / 1000.0;
            long   sent       = Interlocked.Read(ref totalSent);
            long   received   = Interlocked.Read(ref totalReceived);
            long   bytes      = Interlocked.Read(ref totalBytes);
            double msgPerSec  = sent / elapsedSec;
            double mbPerSec   = bytes / 1024.0 / 1024.0 / elapsedSec;
            double receiveRate = sent > 0 ? received * 100.0 / sent : 0;

            bool ok = received > 0;
            TestHelpers.Print(ok,
                $"Duration={elapsedSec:0.0}s  Clients={stressClients.Count}  " +
                $"Sent={sent:N0}  Received={received:N0}  Rate={receiveRate:0.0}%");
            Console.WriteLine($"  Throughput : {msgPerSec:0.0} msg/s  {mbPerSec:0.00} MB/s  " +
                              $"({mbPerSec * 8:0.00} Mbps)");

            // Echo RTT
            lock (echoGate)
            {
                if (echoRtts.Count > 0)
                {
                    double min = echoRtts.Min() / 1000.0;
                    double avg = echoRtts.Average() / 1000.0;
                    double max = echoRtts.Max() / 1000.0;
                    double p99 = echoRtts.OrderBy(x => x).ElementAt((int)(echoRtts.Count * 0.99)) / 1000.0;
                    Console.WriteLine($"  Echo RTT   : min={min:0.00}ms  avg={avg:0.00}ms  p99={p99:0.00}ms  max={max:0.00}ms  ({echoRtts.Count} samples)");
                }
            }

            // Connection stats aggregate
            lock (statGate)
            {
                if (statPings.Count > 0)
                {
                    Console.WriteLine($"  GNS Stats  : Ping min={statPings.Min()}ms avg={statPings.Average():0.0}ms max={statPings.Max()}ms" +
                                      $"  LossLocal avg={statLossLocal.Average() * 100:0.00}%");
                    Console.WriteLine($"  Bandwidth  : OutAvg={statOutBytes.Average() / 1024:0.0}KB/s  InAvg={statInBytes.Average() / 1024:0.0}KB/s  (per-connection, {statPings.Count} samples)");
                }
            }

            // Per-second throughput table
            List<(double, long, long)> snaps;
            lock (snapGate) snaps = snapshots.ToList();
            if (snaps.Count > 0)
            {
                Console.WriteLine("  Per-second : " +
                    string.Join("  ", snaps.Select(s => $"[{s.Item1:0.0}s S:{s.Item2:N0} R:{s.Item3:N0}]")));
            }

            return ok;
        }

        private static void senderJoin(Thread[] threads)
        {
            foreach (var t in threads) t.Join();
        }
    }
}
