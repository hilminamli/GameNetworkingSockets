using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace GameNetworkingSockets.Tests.Cases
{
    public class ReliabilityTest : ITest
    {
        public string Name => "Reliability / Order Preservation";
        public bool LongRunning => false;

        public bool Run(TestContext ctx)
        {
            var server = ctx.Server!;
            var clients = ctx.Clients;

            TestHelpers.Drain(server, clients);

            const int COUNT = 100;
            const string TAG = "ORDER:";
            var client0 = clients[0];
            uint client0Handle = ctx.HandleByClientIndex.TryGetValue(0, out var h) ? h : 0u;

            for (int i = 0; i < COUNT; i++)
            {
                var payload = Encoding.UTF8.GetBytes($"{TAG}{i}");
                _ = client0.SendMessage(payload, SendType.Reliable);
            }

            var received = new List<int>(COUNT);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000 && received.Count < COUNT)
            {
                TestHelpers.PumpAll(server, clients);
                server.ReceiveMessages((hConn, data) =>
                {
                    var s = Encoding.UTF8.GetString(data);
                    if (s.StartsWith(TAG, StringComparison.Ordinal) &&
                        int.TryParse(s.AsSpan(TAG.Length), out int n) &&
                        hConn == client0Handle)
                    {
                        received.Add(n);
                    }
                });
                Thread.Sleep(1);
            }

            int inOrder = 0;
            int gaps = 0;
            int expected = 0;
            foreach (int n in received)
            {
                if (n == expected) { inOrder++; expected++; }
                else { gaps++; expected = n + 1; }
            }

            bool ok = received.Count == COUNT && gaps == 0 && inOrder == COUNT;
            TestHelpers.Print(ok,
                ok ? $"{inOrder}/{COUNT} messages in order"
                   : $"{inOrder}/{COUNT} in order, {gaps} gaps, {received.Count} total received");
            return ok;
        }
    }
}
