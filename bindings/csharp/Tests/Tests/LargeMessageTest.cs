using System;
using System.Diagnostics;
using System.Threading;

namespace GameNetworkingSockets.Tests.Cases
{
    public class LargeMessageTest : ITest
    {
        public string Name => "Large Message (64KB)";
        public bool LongRunning => false;

        public bool Run(TestContext ctx)
        {
            var server = ctx.Server!;
            var clients = ctx.Clients;

            TestHelpers.Drain(server, clients);

            const int SIZE = 65536;
            var payload = new byte[SIZE];
            new Random(42).NextBytes(payload);

            var client0 = clients[0];
            uint client0Handle = ctx.HandleByClientIndex.TryGetValue(0, out var h) ? h : 0u;

            var result = client0.SendMessage(payload, SendType.Reliable);

            byte[]? captured = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000 && captured is null)
            {
                TestHelpers.PumpAll(server, clients);
                server.ReceiveMessages((hConn, data) =>
                {
                    if (data.Length == SIZE && hConn == client0Handle && captured is null)
                        captured = data.ToArray();
                });
                Thread.Sleep(1);
            }

            bool match = captured is not null && captured.Length == SIZE;
            if (match)
            {
                for (int i = 0; i < SIZE; i++)
                {
                    if (captured![i] != payload[i]) { match = false; break; }
                }
            }

            bool ok = result == EResult.OK && match;
            TestHelpers.Print(ok,
                ok ? $"Data integrity verified ({SIZE} bytes)"
                   : $"Large message failed (send={result}, received={(captured?.Length.ToString() ?? "null")} bytes, match={match})");
            return ok;
        }
    }
}
