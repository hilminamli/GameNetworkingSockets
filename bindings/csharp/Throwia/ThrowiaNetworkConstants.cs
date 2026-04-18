/// <summary>Compile-time constants for Throwia network layer buffer sizes and tuning parameters.</summary>
internal static class ThrowiaNetworkConstants
{
    /// <summary>Max messages processed per Tick. Must match the _msgBuffer array size.</summary>
    internal const int MessageBufferSize  = 64;
    /// <summary>Sessions stats are updated for (sessionCount / StatsBatchDivisor) sessions per tick to spread CPU load evenly.</summary>
    internal const int StatsBatchDivisor  = 30;
}
