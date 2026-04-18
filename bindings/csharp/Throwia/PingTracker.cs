using System;

/// <summary>Tracks per-connection ping statistics: last ping, rolling average, and jitter (mean absolute deviation).</summary>
public sealed class PingTracker
{
    private const int SampleCount = 20;
    private readonly int[] _samples = new int[SampleCount];
    private int _head;
    private int _filled;

    public int LastPing    { get; private set; }
    public int AveragePing { get; private set; }
    public int Jitter      { get; private set; }

    /// <summary>Records a new ping sample and recomputes average and jitter.</summary>
    public void Record(int ping)
    {
        LastPing = ping;

        _samples[_head] = ping;
        _head = (_head + 1) % SampleCount;
        if (_filled < SampleCount) _filled++;

        int sum = 0;
        for (int i = 0; i < _filled; i++) sum += _samples[i];
        int avg = sum / _filled;
        AveragePing = avg;

        int dev = 0;
        for (int i = 0; i < _filled; i++) dev += Math.Abs(_samples[i] - avg);
        Jitter = dev / _filled;
    }
}
