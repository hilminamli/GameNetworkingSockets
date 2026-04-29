using System;

namespace GameNetworkingSockets.Tests.Cases
{
    public class StatsTest : ITest
    {
        public string Name => "Connection Statistics";
        public bool LongRunning => false;

        public bool Run(TestContext ctx)
        {
            var server = ctx.Server!;
            var clients = ctx.Clients;

            TestHelpers.PumpFor(200, server, clients);
            bool anyFailure = false;

            for (int i = 0; i < clients.Count; i++)
            {
                if (!ctx.HandleByClientIndex.TryGetValue(i, out uint hConn))
                {
                    Console.WriteLine($"  Client {i}: <no handle tracked>");
                    anyFailure = true;
                    continue;
                }

                bool got = server.GetConnectionStats(hConn, out ConnectionStats s);
                if (got)
                {
                    string lossRemote = s.PacketLossRemote is >= 0f and <= 1f ? $"{s.PacketLossRemote * 100:0.0}%" : "N/A";
                    Console.WriteLine(
                        $"  Client {i}: Ping={s.PingMs}ms  LossLocal={s.PacketLossLocal * 100:0.0}%  LossRemote={lossRemote}  " +
                        $"Out={s.OutBytesPerSec:0}B/s ({s.OutPacketsPerSec:0.0}pkt/s)  " +
                        $"In={s.InBytesPerSec:0}B/s ({s.InPacketsPerSec:0.0}pkt/s)  " +
                        $"Capacity={s.SendRateBytesPerSecond / 1024}KB/s  " +
                        $"PendingRel={s.PendingReliable}B  UnackedRel={s.SentUnackedReliable}B  Queue={s.QueueTimeMicroseconds}\u00b5s");
                }
                else
                {
                    Console.WriteLine($"  Client {i}: <GetConnectionStats failed>");
                    anyFailure = true;
                }
            }

            TestHelpers.Print(!anyFailure, anyFailure ? "one or more stats queries failed" : "stats retrieved for all clients");
            return !anyFailure;
        }
    }
}
