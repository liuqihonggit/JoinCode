namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 注意力涣散检测配置 — 上下文压缩次数 + 运行时长 + 错误决策率三阈值。
/// </summary>
public sealed record AttentionFatigueConfig {
    /// <summary>默认配置 — 压缩>3次 且 运行>2小时 且 错误率>30%</summary>
    public static readonly AttentionFatigueConfig Default = new();

    /// <summary>上下文压缩次数阈值，默认3次</summary>
    public int CompactionThreshold { get; init; } = 3;

    /// <summary>运行时长阈值，默认2小时</summary>
    public TimeSpan DurationThreshold { get; init; } = TimeSpan.FromHours(2);

    /// <summary>错误决策率阈值（0~1），默认0.3（30%）</summary>
    public double ErrorRateThreshold { get; init; } = 0.3;
}

/// <summary>
/// 注意力涣散检测器 — 监测上下文压缩次数 + 运行时长 + 错误决策率。
/// <para>
/// 上下文太多时经历数小时运行多次上下文压缩摘要，会产生注意力涣散状态。
/// 压缩次数超阈值 且 运行时长超阈值 且 错误率超阈值 → 触发注意力涣散，
/// 注入"建议用 /clear 重置上下文或开启新会话"提示。
/// 全局静态状态，生命周期为整个进程。/clear 调用 Reset 重置。
/// </para>
/// </summary>
public static class AttentionFatigueDetector {
    private static int _compactionCount;
    private static int _errorDecisionCount;
    private static int _totalDecisionCount;
    private static DateTime _sessionStart = DateTime.UtcNow;

    /// <summary>当前上下文压缩次数</summary>
    public static int CompactionCount => Volatile.Read(ref _compactionCount);

    /// <summary>当前错误决策次数</summary>
    public static int ErrorDecisionCount => Volatile.Read(ref _errorDecisionCount);

    /// <summary>记录一次上下文压缩 — 递增压缩计数</summary>
    public static void RecordCompaction() => Interlocked.Increment(ref _compactionCount);

    /// <summary>记录一次错误决策 — 递增错误计数和总决策计数</summary>
    public static void RecordErrorDecision() {
        Interlocked.Increment(ref _errorDecisionCount);
        Interlocked.Increment(ref _totalDecisionCount);
    }

    /// <summary>记录一次正常决策 — 递增总决策计数</summary>
    public static void RecordDecision() => Interlocked.Increment(ref _totalDecisionCount);

    /// <summary>
    /// 是否处于注意力涣散状态 — 压缩次数超阈值 且 运行时长超阈值 且 错误率超阈值。
    /// </summary>
    /// <param name="config">检测配置（可选，默认 Default）</param>
    /// <returns>true 表示注意力涣散应提示；false 表示正常</returns>
    public static bool IsFatigued(AttentionFatigueConfig? config = null) {
        var cfg = config ?? AttentionFatigueConfig.Default;

        if (Volatile.Read(ref _compactionCount) <= cfg.CompactionThreshold)
            return false;

        if (DateTime.UtcNow - _sessionStart < cfg.DurationThreshold)
            return false;

        var total = Volatile.Read(ref _totalDecisionCount);
        if (total == 0)
            return false;

        var errorRate = (double)Volatile.Read(ref _errorDecisionCount) / total;
        return errorRate >= cfg.ErrorRateThreshold;
    }

    /// <summary>
    /// 获取注意力涣散提示词 — 建议写交接文档 + 用 /clear 重置上下文或开启新会话。
    /// </summary>
    /// <returns>涣散提示词</returns>
    public static string GetFatiguePrompt() =>
        "\n\n⚠️ 检测到长时间运行出现注意力涣散，请写一个交接文档，放弃所有commit信息(git历史可以查看)，" +
        "只保留主目标和当前目标，然后用 /clear 重置上下文或开启新会话，以恢复注意力集中。";

    /// <summary>
    /// 重置全部状态 — 仅供 /clear 调用，清除压缩计数、错误计数和会话起始时间。
    /// </summary>
    public static void Reset() {
        Interlocked.Exchange(ref _compactionCount, 0);
        Interlocked.Exchange(ref _errorDecisionCount, 0);
        Interlocked.Exchange(ref _totalDecisionCount, 0);
        _sessionStart = DateTime.UtcNow;
    }
}
