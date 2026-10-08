using System;

namespace GameNetworkingSockets.Tests.Cases
{
    /// <summary>
    /// GetConnectionFlags reads SteamNetConnectionInfo_t::m_nFlags at the right offset: a client
    /// dialled by IP on localhost is Unauthenticated (an IP identity is never authenticated) and
    /// Fast (a loopback address), and never Relayed. A wrong offset lands in the description
    /// string or the reserved words and reads something else.
    /// </summary>
    public class ConnectionFlagsTest : ITest
    {
        public string Name => "Connection Flags";
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

                if (!server.GetConnectionFlags(hConn, out ConnectionFlags flags))
                {
                    Console.WriteLine($"  Client {i}: <GetConnectionFlags failed>");
                    anyFailure = true;
                    continue;
                }

                bool expected = (flags & ConnectionFlags.Unauthenticated) != 0
                    && (flags & ConnectionFlags.Fast) != 0
                    && (flags & ConnectionFlags.Relayed) == 0;
                Console.WriteLine($"  Client {i}: {flags} ({(int)flags}){(expected ? "" : "  <expected Unauthenticated | Fast, not Relayed>")}");
                if (!expected) anyFailure = true;
            }

            TestHelpers.Print(!anyFailure, anyFailure ? "flags missing or misread" : "loopback connections read as Unauthenticated | Fast");
            return !anyFailure;
        }
    }
}
