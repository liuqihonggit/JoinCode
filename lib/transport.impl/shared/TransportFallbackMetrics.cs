namespace JoinCode.Transport;

/// <summary>
/// 单个传输的连接统计 — 尝试/成功/失败次数的三元组
/// </summary>
public struct TransportStat {
    /// <summary>连接尝试次数</summary>
    public int Attempts;
    /// <summary>连接成功次数</summary>
    public int Successes;
    /// <summary>连接失败次数</summary>
    public int Failures;
}

/// <summary>
/// 传输回退指标 — 记录各传输的连接尝试、成功、失败次数及回退统计
/// </summary>
public sealed class TransportFallbackMetrics {
    private readonly int _transportCount;
    private readonly TransportStat[] _stats;
    private int _totalFallbacks;
    private long _totalFallbackDurationMs;

    /// <summary>
    /// 构造回退指标收集器
    /// </summary>
    /// <param name="transportCount">传输数量，需大于 0</param>
    public TransportFallbackMetrics(int transportCount) {
        if (transportCount < 1) throw new ArgumentOutOfRangeException(nameof(transportCount));
        _transportCount = transportCount;
        _stats = new TransportStat[transportCount];
    }

    /// <summary>
    /// 记录一次成功的连接尝试
    /// </summary>
    /// <param name="transportIndex">传输索引，范围 [0, transportCount)</param>
    public void RecordConnection(int transportIndex) {
        ValidateIndex(transportIndex);
        Interlocked.Increment(ref _stats[transportIndex].Attempts);
        Interlocked.Increment(ref _stats[transportIndex].Successes);
    }

    /// <summary>
    /// 记录一次失败的连接尝试
    /// </summary>
    /// <param name="transportIndex">传输索引，范围 [0, transportCount)</param>
    public void RecordFailure(int transportIndex) {
        ValidateIndex(transportIndex);
        Interlocked.Increment(ref _stats[transportIndex].Attempts);
        Interlocked.Increment(ref _stats[transportIndex].Failures);
    }

    /// <summary>
    /// 记录一次回退事件
    /// </summary>
    /// <param name="fromIndex">回退前传输索引</param>
    /// <param name="toIndex">回退后传输索引</param>
    /// <param name="durationMs">回退耗时（毫秒）</param>
    public void RecordFallback(int fromIndex, int toIndex, long durationMs) {
        ValidateIndex(fromIndex);
        ValidateIndex(toIndex);
        Interlocked.Increment(ref _totalFallbacks);
        Interlocked.Add(ref _totalFallbackDurationMs, durationMs);
    }

    /// <summary>
    /// 获取当前指标快照（线程安全拷贝）
    /// </summary>
    /// <returns>包含当前统计数据的快照</returns>
    public TransportFallbackMetricsSnapshot GetSnapshot() {
        var stats = new TransportStat[_transportCount];
        for (var i = 0; i < _transportCount; i++) {
            stats[i] = new TransportStat {
                Attempts = Volatile.Read(ref _stats[i].Attempts),
                Successes = Volatile.Read(ref _stats[i].Successes),
                Failures = Volatile.Read(ref _stats[i].Failures),
            };
        }

        var totalFallbacks = Volatile.Read(ref _totalFallbacks);
        var totalDuration = Volatile.Read(ref _totalFallbackDurationMs);

        return new TransportFallbackMetricsSnapshot {
            TransportStats = stats,
            TotalFallbacks = totalFallbacks,
            AverageFallbackDurationMs = totalFallbacks > 0 ? (double)totalDuration / totalFallbacks : 0,
            SnapshotTime = DateTimeOffset.UtcNow,
        };
    }

    private void ValidateIndex(int index) {
        if (index < 0 || index >= _transportCount)
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} out of range [0, {_transportCount})");
    }
}

/// <summary>
/// 传输回退指标快照 — 不可变统计快照
/// </summary>
public sealed class TransportFallbackMetricsSnapshot {
    /// <summary>各传输的连接统计(尝试/成功/失败)</summary>
    public required TransportStat[] TransportStats { get; init; }
    /// <summary>总回退次数</summary>
    public required int TotalFallbacks { get; init; }
    /// <summary>平均回退耗时（毫秒）</summary>
    public required double AverageFallbackDurationMs { get; init; }
    /// <summary>快照时间戳</summary>
    public required DateTimeOffset SnapshotTime { get; init; }
}
