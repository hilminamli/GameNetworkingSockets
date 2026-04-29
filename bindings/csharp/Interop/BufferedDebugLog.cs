using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GameNetworkingSockets
{
    /// <summary>
    /// Thread-safe ring-style buffer for GNS debug output. Native fires the debug callback on a
    /// GNS worker thread, so the application cannot do I/O or call back into engine APIs there
    /// directly. <see cref="Enqueue"/> is the worker-thread-safe sink; <see cref="Drain"/> must
    /// run on the application's main thread (e.g. each tick) to flush messages out.
    ///
    /// The queue is bounded: when it exceeds <see cref="MaxDepth"/>, the oldest message is
    /// dropped and <see cref="DroppedCount"/> is incremented. This caps memory use even if the
    /// application stops draining — the buffer cannot leak unboundedly.
    /// </summary>
    public sealed class BufferedDebugLog
    {
        /// <summary>Default cap. ~10k messages ≈ 5 MB at 500 B/msg.</summary>
        public const int DefaultMaxDepth = 10_000;

        private readonly ConcurrentQueue<(DebugOutputType, string)> _queue = new ConcurrentQueue<(DebugOutputType, string)>();
        private long _droppedCount;

        /// <summary>Maximum queued messages before oldest entries are dropped.</summary>
        public int MaxDepth { get; }

        /// <summary>Total messages dropped because the queue was full. Monotonically increasing.</summary>
        public long DroppedCount => Interlocked.Read(ref _droppedCount);

        /// <param name="maxDepth">Queue cap. Once exceeded, the oldest message is dropped per enqueue.</param>
        public BufferedDebugLog(int maxDepth = DefaultMaxDepth)
        {
            if (maxDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "Must be greater than zero.");
            MaxDepth = maxDepth;
        }

        /// <summary>Worker-thread-safe sink. Pass this to <see cref="NetworkingLibrary.SetDebugOutput"/>.</summary>
        public void Enqueue(DebugOutputType level, string message)
        {
            _queue.Enqueue((level, message));
            while (_queue.Count > MaxDepth && _queue.TryDequeue(out _))
                _ = Interlocked.Increment(ref _droppedCount);
        }

        /// <summary>
        /// Drains all queued messages and feeds them to <paramref name="sink"/>. Call regularly on
        /// the main thread to avoid drops. Returns the number of messages drained.
        /// </summary>
        public int Drain(Action<DebugOutputType, string> sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));

            int n = 0;
            while (_queue.TryDequeue(out var item))
            {
                sink(item.Item1, item.Item2);
                n++;
            }
            return n;
        }
    }
}
