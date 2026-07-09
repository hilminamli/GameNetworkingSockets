using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GameNetworkingSockets;
using GameNetworkingSockets.Transport;

namespace GameNetworkingSockets.TestsP2P;

/// <summary>
/// End-to-end P2P/ICE test over the transport layer — proves that the ICE-enabled native build,
/// the C# P2P surface, and the ClientTransport/ServerTransport P2P modes establish a connection
/// without any IP address being dialed directly.
///
/// Two real processes (GNS identity is per-process): "host" runs ServerTransport.P2P, "client"
/// connects with ClientTransport.P2P. Rendezvous blobs travel over TcpSignalingChannel — a
/// trivial local TCP implementation of ISignalingChannel, a stand-in for the future Nexus lobby.
/// Payload check: client sends "ping", host answers "pong". Default mode spawns both children
/// and reports a verdict.
/// </summary>
internal static class Program
{
    private const int    SignalPort  = 27200;
    private const int    VirtualPort = 7;
    private const string HostId      = "p2p-test-host";
    private const string ClientId    = "p2p-test-client";

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "orchestrate";
        return mode switch
        {
            "host"   => RunHost(),
            "client" => RunClient(),
            _        => Orchestrate(),
        };
    }

    // ── Orchestrator ─────────────────────────────────────────────────────────────

    private static int Orchestrate()
    {
        Console.WriteLine("=== GNS P2P/ICE end-to-end test (transport layer, custom signaling over local TCP) ===");
        string exe = Environment.ProcessPath!;

        using var host = Spawn(exe, "host");
        Thread.Sleep(500); // let the host bind the signaling port
        using var client = Spawn(exe, "client");

        bool clientDone = client.WaitForExit(30_000);
        bool hostDone   = host.WaitForExit(10_000);
        if (!clientDone) TryKill(client);
        if (!hostDone)   TryKill(host);

        bool pass = clientDone && hostDone && client.ExitCode == 0 && host.ExitCode == 0;
        Console.WriteLine(pass
            ? "\n=== P2P TEST PASSED — ICE connection established, ping/pong verified ==="
            : $"\n=== P2P TEST FAILED (host exit={ExitOf(host)}, client exit={ExitOf(client)}) ===");
        return pass ? 0 : 1;
    }

    private static Process Spawn(string exe, string mode)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(exe, mode)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            },
        };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine($"[{mode}] {e.Data}"); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) Console.WriteLine($"[{mode}!] {e.Data}"); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static void TryKill(Process p) { try { p.Kill(); } catch { /* already gone */ } }
    private static string ExitOf(Process p) { try { return p.HasExited ? p.ExitCode.ToString() : "running"; } catch { return "?"; } }

    // ── Host ─────────────────────────────────────────────────────────────────────

    private static int RunHost()
    {
        if (!Init(HostId)) return 2;

        var listener = new TcpListener(IPAddress.Loopback, SignalPort);
        listener.Start();
        Console.WriteLine("signaling: waiting for client pipe...");
        using var tcp = listener.AcceptTcpClient();
        using var channel = new TcpSignalingChannel(tcp.GetStream());
        Console.WriteLine("signaling: pipe up");

        using var transport = ServerTransport.P2P(channel, VirtualPort);
        bool gotPing = false, peerClosed = false;
        transport.OnConnected += conn =>
        {
            Console.WriteLine("P2P peer CONNECTED");
            conn.OnMessage += data =>
            {
                string msg = Encoding.UTF8.GetString(data);
                Console.WriteLine($"recv: \"{msg}\"");
                if (msg == "ping")
                {
                    gotPing = true;
                    conn.Send(Encoding.UTF8.GetBytes("pong"));
                    Console.WriteLine("sent: \"pong\"");
                }
            };
        };
        transport.OnDisconnected += _ => peerClosed = true;
        transport.Start();

        long deadline = Environment.TickCount64 + 25_000;
        while (Environment.TickCount64 < deadline)
        {
            transport.Tick();
            if (gotPing && peerClosed) break; // client got its pong and hung up
            Thread.Sleep(5);
        }

        Console.WriteLine(gotPing ? "host done — ping received, pong sent" : "host TIMEOUT — no ping");
        return gotPing ? 0 : 3;
    }

    // ── Client ───────────────────────────────────────────────────────────────────

    private static int RunClient()
    {
        if (!Init(ClientId)) return 2;

        using var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, SignalPort);
        using var channel = new TcpSignalingChannel(tcp.GetStream());
        Console.WriteLine("signaling: pipe up");

        using var transport = ClientTransport.P2P(channel, HostId, VirtualPort);
        bool connected = false, gotPong = false;
        transport.OnConnected += () =>
        {
            connected = true;
            Console.WriteLine("P2P CONNECTED — sending \"ping\"");
            transport.Send(Encoding.UTF8.GetBytes("ping"));
        };
        transport.OnMessage += data =>
        {
            string msg = Encoding.UTF8.GetString(data);
            Console.WriteLine($"recv: \"{msg}\"");
            if (msg == "pong") gotPong = true;
        };

        if (!transport.Connect())
        {
            Console.WriteLine("FATAL: P2P connect returned no connection");
            return 2;
        }
        Console.WriteLine("P2P connect issued — negotiating via ICE...");

        long deadline = Environment.TickCount64 + 20_000;
        while (Environment.TickCount64 < deadline && !gotPong)
        {
            transport.Tick();
            Thread.Sleep(5);
        }

        if (gotPong)
        {
            Console.WriteLine("SUCCESS — ping/pong over P2P/ICE");
            transport.Disconnect();
            Thread.Sleep(200); // let the close reach the host
            return 0;
        }
        Console.WriteLine(connected ? "TIMEOUT — connected but no pong" : "TIMEOUT — never connected (ICE failed?)");
        return 3;
    }

    // ── Common plumbing ──────────────────────────────────────────────────────────

    private static bool Init(string identity)
    {
        if (!NetworkingLibrary.Initialize(identity, out string? err))
        {
            Console.WriteLine($"FATAL: GNS init failed: {err}");
            return false;
        }

        // ICE is off by default in the open-source build — enable all candidate classes.
        // No STUN/TURN configured: same-machine test connects via local host candidates.
        _ = NetworkingLibrary.SetGlobalConfig(NetworkingConfigValue.P2P_Transport_ICE_Enable, IceEnable.All);

        // Surface GNS internals so an ICE failure is diagnosable from the console.
        // 17 = k_ESteamNetworkingConfig_LogLevel_P2PRendezvous (not yet in the enum) —
        // verbose rendezvous logging, same as Valve's test_p2p.
        _ = NetworkingLibrary.SetGlobalConfig((NetworkingConfigValue)17, (int)DebugOutputType.Verbose);
        var log = new BufferedDebugLog();
        NetworkingLibrary.SetDebugOutput(DebugOutputType.Verbose, log);
        var drain = new Thread(() =>
        {
            while (true) { _ = log.Drain((lvl, m) => Console.WriteLine($"[gns:{lvl}] {m}")); Thread.Sleep(50); }
        }) { IsBackground = true };
        drain.Start();

        Console.WriteLine($"GNS up, identity \"{identity}\"");
        return true;
    }
}

/// <summary>
/// ISignalingChannel over a raw TCP stream with [4-byte length][blob] framing — the test
/// stand-in for the future Nexus lobby channel. This test has exactly one peer on the other
/// end of the pipe, so <c>toIdentity</c> is not used for routing; a real lobby implementation
/// forwards the blob to the named peer.
/// </summary>
internal sealed class TcpSignalingChannel : ISignalingChannel, IDisposable
{
    private readonly NetworkStream _pipe;
    private readonly object _sendLock = new();
    private readonly Thread _reader;

    public event Action<byte[]>? BlobReceived;

    public TcpSignalingChannel(NetworkStream pipe)
    {
        _pipe = pipe;
        _reader = new Thread(ReadLoop) { IsBackground = true };
        _reader.Start();
    }

    /// <summary>May be called from GNS's service thread — serialized by the send lock.</summary>
    public void Send(string toIdentity, byte[] blob)
    {
        lock (_sendLock)
        {
            Span<byte> len = stackalloc byte[4];
            BitConverter.TryWriteBytes(len, blob.Length);
            _pipe.Write(len);
            _pipe.Write(blob);
            _pipe.Flush();
        }
    }

    private void ReadLoop()
    {
        try
        {
            var lenBuf = new byte[4];
            while (true)
            {
                ReadExactly(lenBuf);
                var blob = new byte[BitConverter.ToInt32(lenBuf)];
                ReadExactly(blob);
                BlobReceived?.Invoke(blob);
            }
        }
        catch { /* pipe closed — test over */ }
    }

    private void ReadExactly(byte[] buf)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = _pipe.Read(buf, read, buf.Length - read);
            if (n <= 0) throw new InvalidOperationException("signaling pipe closed");
            read += n;
        }
    }

    public void Dispose() { try { _pipe.Dispose(); } catch { } }
}
