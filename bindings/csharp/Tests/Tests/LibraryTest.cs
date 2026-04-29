using System;

namespace GameNetworkingSockets.Tests.Cases
{
    public class LibraryTest : ITest
    {
        public string Name => "Library Lifecycle";
        public bool LongRunning => false;

        public bool Run(TestContext ctx)
        {
            bool allOk = true;

            NetworkingLibrary.Kill();
            bool killOk = !NetworkingLibrary.IsInitialized;
            TestHelpers.Print(killOk, $"Kill: IsInitialized={NetworkingLibrary.IsInitialized}");
            allOk &= killOk;

            bool initOk = NetworkingLibrary.Initialize(out string err1);
            TestHelpers.Print(initOk, initOk ? "Initialize OK" : $"Initialize failed: {err1}");
            allOk &= initOk;

            NetworkingLibrary.Kill();
            bool killOk2 = !NetworkingLibrary.IsInitialized;
            TestHelpers.Print(killOk2, $"Re-kill: IsInitialized={NetworkingLibrary.IsInitialized}");
            allOk &= killOk2;

            bool reInit = NetworkingLibrary.Initialize(out string err2);
            TestHelpers.Print(reInit, reInit ? "Re-initialize OK" : $"Re-initialize failed: {err2}");
            allOk &= reInit;

            return allOk;
        }
    }
}
