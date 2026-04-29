using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace GameNetworkingSockets.Tests.Cases
{
    public class SendReceiveTest : ITest
    {
        public string Name => "Send/Receive (all SendTypes)";
        public bool LongRunning => false;

        private static readonly SendType[] Types =
        {
            SendType.Reliable,
            SendType.ReliableNoNagle,
            SendType.Unreliable,
            SendType.UnreliableNoNagle,
            SendType.UnreliableNoDelay,
        };

        public bool Run(TestContext ctx)
        {
            var server = ctx.Server!;
            var clients = ctx.Clients;

            bool allOk = true;

            Console.WriteLine("  -- Client -> Server --");
            const int MSGS = 10;
            int expectedPerType = clients.Count * MSGS;

            foreach (var st in Types)
            {
                TestHelpers.Drain(server, clients);

                int received = 0;
                object gate = new();
                var prefix = $"[{st}] ";

                for (int ci = 0; ci < clients.Count; ci++)
                {
                    for (int m = 0; m < MSGS; m++)
                    {
                        var payload = Encoding.UTF8.GetBytes($"{prefix}msg {m} from client {ci}");
                        _ = clients[ci].SendMessage(payload, st);
                    }
                }

                var sw = Stopwatch.StartNew();
                bool reliable = st == SendType.Reliable || st == SendType.ReliableNoNagle;
                while (sw.ElapsedMilliseconds < 2000)
                {
                    TestHelpers.PumpAll(server, clients);
                    server.ReceiveMessages((_, data) =>
                    {
                        var s = Encoding.UTF8.GetString(data);
                        if (s.StartsWith(prefix, System.StringComparison.Ordinal))
                            lock (gate) received++;
                    });
                    if (reliable && received >= expectedPerType) break;
                    Thread.Sleep(1);
                }

                bool ok = reliable ? received == expectedPerType : true;
                double pct = expectedPerType == 0 ? 100.0 : received * 100.0 / expectedPerType;
                string line = reliable
                    ? $"SendType.{st,-17} : {received}/{expectedPerType} received"
                    : $"SendType.{st,-17} : {received}/{expectedPerType} received ({pct:0.0}%) [loss allowed]";
                TestHelpers.Print(ok, line);
                allOk &= ok;
            }

            Console.WriteLine("  -- Server -> Clients (Broadcast) --");
            const int BCAST = 10;
            foreach (var st in Types)
            {
                TestHelpers.Drain(server, clients);

                var prefix = $"[BCAST:{st}] ";
                var perClient = new int[clients.Count];

                for (int m = 0; m < BCAST; m++)
                {
                    var payload = Encoding.UTF8.GetBytes($"{prefix}msg {m}");
                    server.Broadcast(payload, st);
                }

                int total = 0;
                int expectedTotal = BCAST * clients.Count;
                bool reliable = st == SendType.Reliable || st == SendType.ReliableNoNagle;

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2000)
                {
                    TestHelpers.PumpAll(server, clients);
                    for (int ci = 0; ci < clients.Count; ci++)
                    {
                        int idx = ci;
                        clients[ci].ReceiveMessages((_, data) =>
                        {
                            var s = Encoding.UTF8.GetString(data);
                            if (s.StartsWith(prefix, System.StringComparison.Ordinal))
                            {
                                Interlocked.Increment(ref perClient[idx]);
                                Interlocked.Increment(ref total);
                            }
                        });
                    }
                    if (reliable && total >= expectedTotal) break;
                    Thread.Sleep(1);
                }

                bool ok = reliable ? perClient.All(n => n == BCAST) : true;
                var perClientStr = string.Join(", ", perClient.Select((n, i) => $"c{i}={n}"));
                TestHelpers.Print(ok, $"SendType.{st,-17} : [{perClientStr}] / {BCAST} each");
                allOk &= ok;
            }

            return allOk;
        }
    }
}
