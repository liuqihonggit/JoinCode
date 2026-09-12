namespace JoinCode.Transport;

public sealed class TransportFallbackMetrics
{
    private readonly int _transportCount;
    private readonly int[] _connectionAttempts;
    private readonly int[] _connectionSuccesses;
    private readonly int[] _connectionFailures;
    private int _totalFallbacks;
    private long _totalFallbackDurationMs;

    public TransportFallbackMetrics(int transportCount)
    {
        if (transportCount < 1) throw new ArgumentOutOfRangeException(nameof(transportCount));
        _transportCount = transportCount;
        _connectionAttempts = new int[transportCount];
        _connectionSuccesses = new int[transportCount];
        _connectionFailures = new int[transportCount];
    }

    public void RecordConnection(int transportIndex)
    {
        ValidateIndex(transportIndex);
        Interlocked.Increment(ref _connectionAttempts[transportIndex]);
        Interlocked.Increment(ref _connectionSuccesses[transportIndex]);
    }

    public void RecordFailure(int transportIndex)
    {
        ValidateIndex(transportIndex);
        Interlocked.Increment(ref _connectionAttempts[transportIndex]);
        Interlocked.Increment(ref _connectionFailures[transportIndex]);
    }

    public void RecordFallback(int fromIndex, int toIndex, long durationMs)
    {
        ValidateIndex(fromIndex);
        ValidateIndex(toIndex);
        Interlocked.Increment(ref _totalFallbacks);
        Interlocked.Add(ref _totalFallbackDurationMs, durationMs);
    }

    public TransportFallbackMetricsSnapshot GetSnapshot()
    {
        var attempts = new int[_transportCount];
        var successes = new int[_transportCount];
        var failures = new int[_transportCount];
        Array.Copy(_connectionAttempts, attempts, _transportCount);
        Array.Copy(_connectionSuccesses, successes, _transportCount);
        Array.Copy(_connectionFailures, failures, _transportCount);

        var totalFallbacks = Volatile.Read(ref _totalFallbacks);
        var totalDuration = Volatile.Read(ref _totalFallbackDurationMs);

        return new TransportFallbackMetricsSnapshot
        {
            ConnectionAttempts = attempts,
            ConnectionSuccesses = successes,
            ConnectionFailures = failures,
            TotalFallbacks = totalFallbacks,
            AverageFallbackDurationMs = totalFallbacks > 0 ? (double)totalDuration / totalFallbacks : 0,
            SnapshotTime = DateTimeOffset.UtcNow,
        };
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= _transportCount)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} out of range [0, {_transportCount})");
    }
}

public sealed class TransportFallbackMetricsSnapshot
{
    public required int[] ConnectionAttempts { get; init; }
    public required int[] ConnectionSuccesses { get; init; }
    public required int[] ConnectionFailures { get; init; }
    public required int TotalFallbacks { get; init; }
    public required double AverageFallbackDurationMs { get; init; }
    public required DateTimeOffset SnapshotTime { get; init; }
}
