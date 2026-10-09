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
/// <para>
/// 内部计数委托给 FrequencyGate（累积窗口=10年，等效于永不过期），
/// 与 ToolQuotaService（高频闹钟）和 LowFrequencyInductionService（低频冷却）共享同一套频率门控基础设施。
/// </para>
/// </summary>
public static class AttentionFatigueDetector {
    private static readonly FrequencyGate Gate = new();
    private static readonly TimeSpan CumulativeWindow = TimeSpan.FromDays(365 * 10);
    private static DateTime _sessionStart = DateTime.UtcNow;

    private const string CompactionKey = "compaction";
    private const string ErrorKey = "error";
    private const string TotalKey = "total";

    /// <summary>当前上下文压缩次数</summary>
    public static int CompactionCount => Gate.CountInWindow(CompactionKey, CumulativeWindow);

    /// <summary>当前错误决策次数</summary>
    public static int ErrorDecisionCount => Gate.CountInWindow(ErrorKey, CumulativeWindow);

    /// <summary>记录一次上下文压缩 — 递增压缩计数</summary>
    public static void RecordCompaction() => Gate.Record(CompactionKey, CumulativeWindow);

    /// <summary>记录一次错误决策 — 递增错误计数和总决策计数</summary>
    public static void RecordErrorDecision() {
        Gate.Record(ErrorKey, CumulativeWindow);
        Gate.Record(TotalKey, CumulativeWindow);
    }

    /// <summary>记录一次正常决策 — 递增总决策计数</summary>
    public static void RecordDecision() => Gate.Record(TotalKey, CumulativeWindow);

    /// <summary>
    /// 是否处于注意力涣散状态 — 压缩次数超阈值 且 运行时长超阈值 且 错误率超阈值。
    /// </summary>
    /// <param name="config">检测配置（可选，默认 Default）</param>
    /// <returns>true 表示注意力涣散应提示；false 表示正常</returns>
    public static bool IsFatigued(AttentionFatigueConfig? config = null) {
        var cfg = config ?? AttentionFatigueConfig.Default;

        if (Gate.CountInWindow(CompactionKey, CumulativeWindow) <= cfg.CompactionThreshold)
            return false;

        if (DateTime.UtcNow - _sessionStart < cfg.DurationThreshold)
            return false;

        var total = Gate.CountInWindow(TotalKey, CumulativeWindow);
        if (total == 0)
            return false;

        var errorRate = (double)Gate.CountInWindow(ErrorKey, CumulativeWindow) / total;
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
        Gate.Reset();
        _sessionStart = DateTime.UtcNow;
    }
}
