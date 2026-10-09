namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具健康监控服务接口 — 追踪工具执行成功率、评分状态
/// 设计原则：永远不禁用工具，连续失败只注入提示词提醒LLM换策略
/// </summary>
public interface IToolHealthMonitor {
    /// <summary>记录工具执行成功。</summary>
    Task<ToolHealthRecord> RecordSuccessAsync(string toolName, CancellationToken ct = default);
    /// <summary>记录工具执行失败。</summary>
    Task<ToolHealthRecord> RecordFailureAsync(string toolName, string? errorMessage, CancellationToken ct = default);
    /// <summary>获取指定工具的健康记录。</summary>
    Task<ToolHealthRecord?> GetRecordAsync(string toolName, CancellationToken ct = default);
    /// <summary>获取所有工具的健康记录字典。</summary>
    Task<IReadOnlyDictionary<string, ToolHealthRecord>> GetAllRecordsAsync(CancellationToken ct = default);
    /// <summary>重置指定工具的健康记录。</summary>
    Task ResetToolAsync(string toolName, CancellationToken ct = default);
    /// <summary>判断指定工具是否在黑名单中。</summary>
    bool IsBlacklisted(string toolName);
    /// <summary>获取指定工具的惩罚值。</summary>
    int GetPenalty(string toolName);
    /// <summary>获取指定工具的有效评分。</summary>
    int GetEffectiveScore(string toolName);
    /// <summary>更新黑名单。</summary>
    void UpdateBlacklist(HashSet<string> newBlacklist);
    /// <summary>更新惩罚字典。</summary>
    void UpdatePenalties(Dictionary<string, int> newPenalties);
    /// <summary>主动加热指定工具 — 设置临时评分增量，ttl 过期后自动回落。</summary>
    Task<ToolHealthRecord> BoostToolAsync(string toolName, int boostScore, TimeSpan ttl, CancellationToken ct = default);
    /// <summary>主动加热所有冷工具（评分低于阈值的工具） — 批量设置临时评分增量。</summary>
    Task<int> HeatColdToolsAsync(int coldThreshold = -20, int boostScore = 30, TimeSpan? ttl = null, CancellationToken ct = default);
    /// <summary>记录工具转移 — 从 fromTool 转移到 toTool，更新转移频率映射。</summary>
    Task RecordTransitionAsync(string fromTool, string toTool, CancellationToken ct = default);
}

/// <summary>
/// 工具健康记录 — 追踪单个工具的执行成功/失败/评分状态
/// </summary>
public sealed record ToolHealthRecord {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取或设置评分。</summary>
    public int Score { get; init; }
    /// <summary>获取或设置成功次数。</summary>
    public int SuccessCount { get; init; }
    /// <summary>获取或设置失败次数。</summary>
    public int FailCount { get; init; }
    /// <summary>获取或设置连续失败次数。</summary>
    public int ConsecutiveFailures { get; init; }
    /// <summary>获取或设置是否启用。</summary>
    public bool IsEnabled { get; init; } = true;
    /// <summary>获取或设置最后调整时间。</summary>
    public DateTime LastAdjusted { get; init; } = DateTime.UtcNow;
    /// <summary>获取或设置最后错误消息。</summary>
    public string? LastErrorMessage { get; init; }
    /// <summary>获取或设置临时加热评分增量（主动加热冷工具时设置，BoostExpiry 过期后清零）。</summary>
    public int BoostScore { get; init; }
    /// <summary>获取或设置临时加热过期时间（UTC），过期后 BoostScore 归零。</summary>
    public DateTime? BoostExpiry { get; init; }
    /// <summary>获取或设置转移频率映射（下一个工具名 → 转移次数），用于运行时学习工具链路。</summary>
    public FrozenDictionary<string, int> NextToolFrequency { get; init; } = FrozenDictionary<string, int>.Empty;

    /// <summary>获取当前是否处于有效加热期（BoostScore > 0 且未过期）。</summary>
    public bool IsBoostActive => BoostScore > 0 && BoostExpiry is { } expiry && DateTime.UtcNow < expiry;

    /// <summary>获取成功率。</summary>
    public double SuccessRate => SuccessCount + FailCount > 0
        ? (double)SuccessCount / (SuccessCount + FailCount) : 0.5;

    /// <summary>
    /// 生成"状态+条件+目的"自然语言状态描述 — 供 tool_score 输出，让 AI 直观理解工具当前状态
    /// </summary>
    /// <param name="chainRecommendations">链路推荐（来自超图评分器），可为 null</param>
    /// <returns>自然语言状态描述字符串</returns>
    public string GenerateStatusDescription(string[]? chainRecommendations = null) {
        var totalCalls = SuccessCount + FailCount;
        var idleHours = (DateTime.UtcNow - LastAdjusted).TotalHours;

        var heatLabel = totalCalls switch {
            >= 50 => "热",
            >= 10 => "温",
            >= 1 => "冷",
            _ => "未使用"
        };

        var healthLabel = SuccessRate switch {
            >= 0.9 => "健康",
            >= 0.7 => "一般",
            >= 0.5 => "不稳定",
            _ => "异常"
        };

        var scoreLabel = Score switch {
            >= 50 => "高评分",
            >= 0 => "正常",
            >= -30 => "低评分",
            _ => "危险"
        };

        var sb = new StringBuilder(256);
        sb.Append($"工具 {ToolName} 共调用 {totalCalls} 次（{heatLabel}），");
        sb.Append($"成功率 {SuccessRate:P0}（{healthLabel}），");
        sb.Append($"评分 {Score}（{scoreLabel}）");

        if (ConsecutiveFailures > 0)
            sb.Append($"，连续失败 {ConsecutiveFailures} 次");

        if (idleHours >= 1)
            sb.Append($"，空闲 {idleHours:F0} 小时");

        if (!IsEnabled)
            sb.Append("，已熔断");

        if (chainRecommendations is { Length: > 0 })
            sb.Append($"，推荐链路 → {string.Join(" → ", chainRecommendations)}");

        return sb.ToString();
    }
}

/// <summary>
/// 工具健康监控扩展方法 — 提供错误追踪判断能力（合并自 ToolErrorTracker）
/// </summary>
public static class ToolHealthMonitorExtensions {
    /// <summary>
    /// 判断工具是否应该自动修正 — 连续失败次数达到阈值
    /// </summary>
    public static async Task<bool> ShouldAutoFixAsync(
        this IToolHealthMonitor monitor,
        string toolName,
        int threshold = 3,
        CancellationToken ct = default) {
        var record = await monitor.GetRecordAsync(toolName, ct).ConfigureAwait(false);
        return record is not null && record.ConsecutiveFailures >= threshold;
    }

    /// <summary>
    /// 获取工具错误次数（= ConsecutiveFailures）
    /// </summary>
    public static async Task<int> GetErrorCountAsync(
        this IToolHealthMonitor monitor,
        string toolName,
        CancellationToken ct = default) {
        var record = await monitor.GetRecordAsync(toolName, ct).ConfigureAwait(false);
        return record?.ConsecutiveFailures ?? 0;
    }
}

/// <summary>
/// 工具评分配置 — 控制奖惩幅度、提示词阈值、时间衰减率
/// </summary>
public sealed class ToolScoreConfig {
    /// <summary>获取或设置成功增量。</summary>
    public int SuccessDelta { get; init; } = 1;
    /// <summary>获取或设置失败增量。</summary>
    public int FailDelta { get; init; } = -5;
    /// <summary>连续失败达到此阈值时注入提示词提醒LLM换策略（不禁用工具）</summary>
    public int WarningThreshold { get; init; } = 3;
    /// <summary>获取或设置评分下限。</summary>
    public int ScoreMin { get; init; } = -100;
    /// <summary>获取或设置评分上限。</summary>
    public int ScoreMax { get; init; } = 100;
    /// <summary>获取或设置每小时衰减率。</summary>
    public double DecayRatePerHour { get; init; } = 0.1;
    /// <summary>获取或设置衰减恢复评分。</summary>
    public int DecayRecoveryScore { get; init; } = 1;
}