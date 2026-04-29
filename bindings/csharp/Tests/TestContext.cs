using System;
using System.Collections.Generic;

namespace GameNetworkingSockets.Tests
{
    public class TestContext : IDisposable
    {
        public NetworkingServer? Server;
        public List<NetworkingClient> Clients = new();
        public Dictionary<int, uint> HandleByClientIndex = new();
        public Dictionary<uint, int> ClientIndexByHandle = new();
        public HashSet<uint> DisconnectedHandles = new();

        public void Dispose()
        {
            foreach (var c in Clients)
            {
                try { c.Dispose(); } catch { }
            }
            Clients.Clear();

            try { Server?.Dispose(); } catch { }
            Server = null;

            HandleByClientIndex.Clear();
            ClientIndexByHandle.Clear();
            DisconnectedHandles.Clear();
        }
    }
}
