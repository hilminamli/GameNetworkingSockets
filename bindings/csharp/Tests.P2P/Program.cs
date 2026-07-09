using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using GameNetworkingSockets;

namespace GameNetworkingSockets.TestsP2P;

/// <summary>
/// End-to-end P2P/ICE test with custom signaling — the proof that the ICE-enabled native
/// build + the C# P2P surface actually establish a connection without any IP address being
/// dialed directly.
///
/// Two real processes (GNS identity is per-process): "host" listens P2P, "client" connects
/// via ConnectP2PCustomSignaling. Rendezvous blobs travel over a trivial local TCP pipe —
/// a stand-in for the future Nexus lobby. Payload check: client sends "ping", host answers
/// "pong". Default mode spawns both children and reports a verdict.
/// </summary>
internal static class Program
{
    private const int    SignalPort  = 27200;
    private const int    VirtualPort = 7;
    private const string HostId      = "p2p-test-host";
    private const string ClientId    = "p2p-test-client";

    // ── Shared signaling state (per process) ────────────────────────────────────

    private static NetworkStream? _pipe;              // TCP pipe to the other process
    private static readonly object _sendLock = new();

    // Inbound rendezvous blobs, moved off the pipe-reader thread. All GNS calls —
    // including ReceivedSignal and the OnConnectRequest it invokes inline — stay on
    // the main thread, so adoption/accept never races the pump loop.
    private static readonly ConcurrentQueue<byte[]> _inbox = new();

    // Rooted delegates — native holds these function pointers for the process lifetime.
    private static FnCustomSignalingSendSignal?       _sendSignal;
    private static FnCustomSignalingOnConnectRequest? _onConnectRequest;
    private static IntPtr _replySignaling;             // host: signaling object answered to incoming requests

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
        Console.WriteLine("=== GNS P2P/ICE end-to-end test (custom signaling over local TCP) ===");
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

        // Signaling pipe: accept the client process.
        var listener = new TcpListener(IPAddress.Loopback, SignalPort);
        listener.Start();
        Console.WriteLine("signaling: waiting for client pipe...");
        using var tcp = listener.AcceptTcpClient();
        _pipe = tcp.GetStream();
        Console.WriteLine("signaling: pipe up");

        // Reply-direction signaling object, handed to GNS when a connect request arrives.
        _sendSignal     = SendSignalOverPipe;
        _replySignaling = P2PSignaling.CreateSignalingObject(_sendSignal);
        if (_replySignaling == IntPtr.Zero) { Console.WriteLine("FATAL: CreateSignalingObject failed"); return 2; }

        using var server = NetworkingServer.P2P(VirtualPort);
        bool gotPing = false, peerClosed = false;
        server.OnClientConnected    += _ => Console.WriteLine("P2P peer CONNECTED");
        server.OnClientDisconnected += (_, _, _) => peerClosed = true;

        // Custom-signaling connections are not tied to the listen socket — adopt them
        // explicitly so status callbacks route to this server and it gets accepted.
        _onConnectRequest = (IntPtr ctx, uint hConn, ref SteamNetworkingIdentity peer, int vport) =>
        {
            var r = server.AdoptP2PConnection(hConn);
            Console.WriteLine($"incoming P2P connect request (vport {vport}) — adopt: {r}");
            return _replySignaling;
        };

        StartPipeReader();

        long deadline = Environment.TickCount64 + 25_000;
        while (Environment.TickCount64 < deadline)
        {
            DrainSignals();
            server.RunCallbacks();
            server.ReceiveMessages((h, data) =>
            {
                string msg = Encoding.UTF8.GetString(data);
                Console.WriteLine($"recv: \"{msg}\"");
                if (msg == "ping")
                {
                    gotPing = true;
                    server.Broadcast(Encoding.UTF8.GetBytes("pong"));
                    Console.WriteLine("sent: \"pong\"");
                }
            });
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
        _pipe = tcp.GetStream();
        Console.WriteLine("signaling: pipe up");

        _sendSignal = SendSignalOverPipe;
        IntPtr signaling = P2PSignaling.CreateSignalingObject(_sendSignal);
        if (signaling == IntPtr.Zero) { Console.WriteLine("FATAL: CreateSignalingObject failed"); return 2; }
        // Client never receives NEW connect requests; reply blobs route to the existing connection.
        _onConnectRequest = (IntPtr ctx, uint hConn, ref SteamNetworkingIdentity peer, int vport) => IntPtr.Zero;

        StartPipeReader();

        using var client = new NetworkingClient();
        bool connected = false, gotPong = false;
        client.OnConnected += () =>
        {
            connected = true;
            Console.WriteLine("P2P CONNECTED — sending \"ping\"");
            _ = client.SendMessage(Encoding.UTF8.GetBytes("ping"));
        };
        client.OnDisconnected += (reason, dbg) => Console.WriteLine($"disconnected: {reason} {dbg}");

        var hostIdentity = P2PSignaling.MakeGenericIdentity(HostId);
        if (!client.ConnectP2PCustomSignaling(signaling, ref hostIdentity, VirtualPort))
        {
            Console.WriteLine("FATAL: ConnectP2PCustomSignaling returned no connection");
            return 2;
        }
        Console.WriteLine("ConnectP2PCustomSignaling issued — negotiating via ICE...");

        long deadline = Environment.TickCount64 + 20_000;
        while (Environment.TickCount64 < deadline && !gotPong)
        {
            DrainSignals();
            client.RunCallbacks();
            client.ReceiveMessages((h, data) =>
            {
                string msg = Encoding.UTF8.GetString(data);
                Console.WriteLine($"recv: \"{msg}\"");
                if (msg == "pong") gotPong = true;
            });
            Thread.Sleep(5);
        }

        if (gotPong)
        {
            Console.WriteLine("SUCCESS — ping/pong over P2P/ICE");
            client.Disconnect();
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

    /// <summary>GNS → pipe: forward a rendezvous blob to the other process. May run on GNS's service thread.</summary>
    private static bool SendSignalOverPipe(IntPtr ctx, uint hConn, IntPtr pInfo, IntPtr pMsg, int cbMsg)
    {
        var blob = new byte[cbMsg];
        Marshal.Copy(pMsg, blob, 0, cbMsg);
        try
        {
            lock (_sendLock)
            {
                Span<byte> len = stackalloc byte[4];
                BitConverter.TryWriteBytes(len, cbMsg);
                _pipe!.Write(len);
                _pipe.Write(blob);
                _pipe.Flush();
            }
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"signal send failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Pipe → inbox: read framed rendezvous blobs off the wire. GNS never touched here.</summary>
    private static void StartPipeReader()
    {
        var t = new Thread(() =>
        {
            try
            {
                var lenBuf = new byte[4];
                while (true)
                {
                    ReadExactly(lenBuf);
                    int len = BitConverter.ToInt32(lenBuf);
                    var blob = new byte[len];
                    ReadExactly(blob);
                    _inbox.Enqueue(blob);
                }
            }
            catch { /* pipe closed — test over */ }
        }) { IsBackground = true };
        t.Start();
    }

    /// <summary>Inbox → GNS, on the pump thread: feed blobs into ReceivedP2PCustomSignal2.</summary>
    private static void DrainSignals()
    {
        while (_inbox.TryDequeue(out var blob))
        {
            if (!P2PSignaling.ReceivedSignal(blob, _onConnectRequest!))
                Console.WriteLine("ReceivedSignal returned false (blob rejected)");
        }
    }

    private static void ReadExactly(byte[] buf)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = _pipe!.Read(buf, read, buf.Length - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
    }
}

internal sealed class EndOfStreamException : Exception { }
