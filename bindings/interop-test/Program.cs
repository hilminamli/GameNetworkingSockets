using System.Text;
using GameNetworkingSockets;

// Cross-language interop harness for the C# binding.
//   CrossTest server <port>          → listen, echo "echo:<msg>" back to sender
//   CrossTest client <host:port>     → connect, send "ping", expect "echo:ping", exit 0
//
// Pairs against the TS binding (bindings/nodejs) which runs the opposite role.
// Both bindings use the same GNS native lib + anonymous identity, so the wire is identical.

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: CrossTest <server|client> <port|host:port>");
    return 2;
}

string mode = args[0];

if (!NetworkingLibrary.Initialize(out string err))
{
    Console.Error.WriteLine($"init failed: {err}");
    return 2;
}

int exitCode = 1;

try
{
    if (mode == "server")
    {
        ushort port = ushort.Parse(args[1]);
        using var server = new NetworkingServer(port);
        Console.WriteLine($"[cs-server] listening on {port}");

        server.OnClientConnected += conn => Console.WriteLine($"[cs-server] client connected: {conn}");
        server.OnClientDisconnected += (conn, reason, dbg) =>
            Console.WriteLine($"[cs-server] client {conn} disconnected reason={reason} {dbg}");

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            server.RunCallbacks();
            server.ReceiveMessages((conn, data) =>
            {
                string msg = Encoding.UTF8.GetString(data);
                Console.WriteLine($"[cs-server] recv \"{msg}\" from {conn} — echoing");
                byte[] reply = Encoding.UTF8.GetBytes("echo:" + msg);
                server.SendMessage(conn, reply, SendType.Reliable);
            });
            Thread.Sleep(15);
        }
        exitCode = 0; // server runs for the window; the client decides PASS/FAIL
    }
    else if (mode == "client")
    {
        using var client = new NetworkingClient();
        bool done = false;

        client.OnConnected += () =>
        {
            Console.WriteLine("[cs-client] connected — sending \"ping\"");
            client.SendMessage(Encoding.UTF8.GetBytes("ping"), SendType.Reliable);
        };
        client.OnDisconnected += (reason, dbg) =>
            Console.WriteLine($"[cs-client] disconnected reason={reason} {dbg}");

        Console.WriteLine($"[cs-client] dialing {args[1]}");
        if (!client.Connect(args[1]))
        {
            Console.Error.WriteLine("[cs-client] connect call failed");
            return 1;
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !done)
        {
            client.RunCallbacks();
            client.ReceiveMessages((conn, data) =>
            {
                string msg = Encoding.UTF8.GetString(data);
                Console.WriteLine($"[cs-client] recv \"{msg}\"");
                if (msg == "echo:ping")
                {
                    Console.WriteLine("CS-CLIENT PASS");
                    exitCode = 0;
                    done = true;
                }
            });
            Thread.Sleep(15);
        }
        if (!done) Console.Error.WriteLine("[cs-client] TIMEOUT — no echo:ping");
    }
    else
    {
        Console.Error.WriteLine($"unknown mode: {mode}");
        exitCode = 2;
    }
}
finally
{
    NetworkingLibrary.Kill();
}

return exitCode;
