using System.Diagnostics;
using System.Linq;

namespace GameNetworkingSockets.Tests.Cases
{
    public class ConnectTest : ITest
    {
        public string Name => "Connect (5 clients)";
        public bool LongRunning => false;

        private const int CLIENT_COUNT = 5;
        private const ushort PORT = 27015;
        private const string ADDR = "127.0.0.1:27015";

        public bool Run(TestContext ctx)
        {
            ctx.Server = new NetworkingServer(PORT);

            ctx.Server.OnClientConnected += hConn =>
            {
                for (int i = 0; i < CLIENT_COUNT; i++)
                {
                    if (!ctx.HandleByClientIndex.ContainsKey(i))
                    {
                        ctx.HandleByClientIndex[i] = hConn;
                        ctx.ClientIndexByHandle[hConn] = i;
                        break;
                    }
                }
            };

            ctx.Server.OnClientDisconnected += (hConn, _, _) =>
            {
                ctx.DisconnectedHandles.Add(hConn);
            };

            for (int i = 0; i < CLIENT_COUNT; i++)
            {
                var c = new NetworkingClient();
                if (!c.Connect(ADDR))
                {
                    TestHelpers.Print(false, $"Client {i} Connect() returned false");
                    return false;
                }
                ctx.Clients.Add(c);
            }

            var sw = Stopwatch.StartNew();
            bool allUp = TestHelpers.WaitUntil(
                () => ctx.Clients.All(c => c.IsConnected) && ctx.Server!.Clients.Count == CLIENT_COUNT,
                timeoutMs: 5000,
                tick: () => TestHelpers.PumpAll(ctx.Server, ctx.Clients));
            sw.Stop();

            TestHelpers.Print(allUp, $"All {CLIENT_COUNT} clients connected in {sw.ElapsedMilliseconds}ms");
            TestHelpers.PumpFor(50, ctx.Server, ctx.Clients);
            return allUp;
        }
    }
}
