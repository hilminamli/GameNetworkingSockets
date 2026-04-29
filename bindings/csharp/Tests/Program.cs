using System;
using System.Linq;
using System.Text;
using GameNetworkingSockets;
using GameNetworkingSockets.Tests;
using GameNetworkingSockets.Tests.Cases;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("=== GNS C# Binding Test Suite ===");
Console.WriteLine();

bool includeLong = args.Any(a => a == "--long" || a == "-l" || a == "--all");

if (!NetworkingLibrary.Initialize(out string initErr))
{
    Console.WriteLine($"FATAL: library init failed: {initErr}");
    Environment.Exit(2);
}

using var ctx = new TestContext();
var runner = new TestRunner(ctx);

runner.Add(new LibraryTest());
runner.Add(new ConnectTest());
runner.Add(new SendReceiveTest());
runner.Add(new ReliabilityTest());
runner.Add(new LargeMessageTest());
runner.Add(new StatsTest());
runner.Add(new OffsetProbeTest());
runner.Add(new DisconnectTest());
runner.Add(new StressTest());
runner.Add(new ScalabilityTest());
runner.Add(new ScalabilityLongTest());
runner.Add(new LeakTest());

int exitCode = runner.Run(includeLong);

ctx.Dispose();
NetworkingLibrary.Kill();
Environment.Exit(exitCode);
