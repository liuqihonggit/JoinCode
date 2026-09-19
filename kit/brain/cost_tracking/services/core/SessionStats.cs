namespace Core.CostTracking;

/// <summary>
/// 会话统计 — 封装行数统计和会话时间
/// 从 CostTracker 提取,纯统计无存储/预算依赖
/// </summary>
internal sealed class CostSessionStats {
    private int _totalLinesAdded;
    private int _totalLinesRemoved;
    private DateTime _sessionStartTime;
    private readonly IClockService _clock;

    /// <summary>构造 — 记录会话开始时间</summary>
    public CostSessionStats(IClockService clock) {
        _clock = clock;
        _sessionStartTime = _clock.GetUtcNow();
    }

    /// <summary>会话开始时间</summary>
    public DateTime SessionStartTime => _sessionStartTime;

    /// <summary>总新增行数</summary>
    public int TotalLinesAdded => Volatile.Read(ref _totalLinesAdded);

    /// <summary>总删除行数</summary>
    public int TotalLinesRemoved => Volatile.Read(ref _totalLinesRemoved);

    /// <summary>当前时间 — 供 RecordUsage 获取时间戳</summary>
    public DateTime CurrentTime => _clock.GetUtcNow();

    /// <summary>记录行数变更</summary>
    public void RecordLinesChanged(int added, int removed) {
        Interlocked.Add(ref _totalLinesAdded, added);
        Interlocked.Add(ref _totalLinesRemoved, removed);
    }

    /// <summary>重置统计</summary>
    public void Reset() {
        Interlocked.Exchange(ref _totalLinesAdded, 0);
        Interlocked.Exchange(ref _totalLinesRemoved, 0);
        _sessionStartTime = _clock.GetUtcNow();
    }
}