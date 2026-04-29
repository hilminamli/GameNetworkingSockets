using System;
using System.Collections.Generic;

namespace GameNetworkingSockets.Tests
{
    public interface ITest
    {
        string Name { get; }
        bool LongRunning { get; }
        bool Run(TestContext ctx);
    }

    public class TestRunner
    {
        private readonly List<ITest> _tests = new();
        private readonly TestContext _ctx;

        public TestRunner(TestContext ctx)
        {
            _ctx = ctx;
        }

        public void Add(ITest test) => _tests.Add(test);

        public int Run(bool includeLongRunning = false)
        {
            int passed = 0;
            int counted = 0;

            for (int i = 0; i < _tests.Count; i++)
            {
                var test = _tests[i];
                Console.WriteLine($"[Test {i + 1}] {test.Name}");

                if (test.LongRunning && !includeLongRunning)
                {
                    Console.WriteLine("  ~ skipped (long-running)");
                    Console.WriteLine();
                    continue;
                }

                counted++;
                bool ok;
                try
                {
                    ok = test.Run(_ctx);
                }
                catch (Exception ex)
                {
                    ok = false;
                    Console.WriteLine($"  \u2717 exception: {ex.GetType().Name}: {ex.Message}");
                }

                if (ok) passed++;
                Console.WriteLine();
            }

            Console.WriteLine($"=== RESULTS: {passed}/{counted} passed ===");
            return passed == counted ? 0 : 1;
        }
    }
}
