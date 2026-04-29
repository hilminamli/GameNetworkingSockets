using System.Linq;

namespace GameNetworkingSockets.Tests.Cases
{
    public class DisconnectTest : ITest
    {
        public string Name => "Disconnect";
        public bool LongRunning => false;

        public bool Run(TestContext ctx)
        {
            var server = ctx.Server!;
            int beforeCount = server.Clients.Count;
            uint client0Handle = ctx.HandleByClientIndex.TryGetValue(0, out var h) ? h : 0u;

            ctx.Clients[0].Dispose();

            bool done = TestHelpers.WaitUntil(
                () => ctx.DisconnectedHandles.Contains(client0Handle) || server.Clients.Count < beforeCount,
                timeoutMs: 5000,
                tick: () => TestHelpers.PumpAll(server, ctx.Clients.Skip(1)));

            int afterCount = server.Clients.Count;
            bool ok = done && afterCount == beforeCount - 1;

            ctx.Clients.RemoveAt(0);
            ctx.HandleByClientIndex.Remove(0);
            ctx.ClientIndexByHandle.Remove(client0Handle);

            TestHelpers.Print(ok,
                ok ? $"Client 0 disconnected, server Clients.Count = {afterCount}"
                   : $"Disconnect failed (before={beforeCount}, after={afterCount}, event fired={ctx.DisconnectedHandles.Contains(client0Handle)})");
            return ok;
        }
    }
}
