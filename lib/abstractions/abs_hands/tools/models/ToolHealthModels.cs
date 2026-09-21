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
}

/// <summary>
/// 工具健康记录 — 追踪单个工具的执行成功/失败/评分状态
/// </summary>
public sealed class ToolHealthRecord {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取或设置评分。</summary>
    public int Score { get; set; }
    /// <summary>获取或设置成功次数。</summary>
    public int SuccessCount { get; set; }
    /// <summary>获取或设置失败次数。</summary>
    public int FailCount { get; set; }
    /// <summary>获取或设置连续失败次数。</summary>
    public int ConsecutiveFailures { get; set; }
    /// <summary>获取或设置是否启用。</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>获取或设置最后调整时间。</summary>
    public DateTime LastAdjusted { get; set; } = DateTime.UtcNow;
    /// <summary>获取或设置最后错误消息。</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>获取成功率。</summary>
    public double SuccessRate => SuccessCount + FailCount > 0
        ? (double)SuccessCount / (SuccessCount + FailCount) : 0.5;
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
    public int SuccessDelta { get; set; } = 1;
    /// <summary>获取或设置失败增量。</summary>
    public int FailDelta { get; set; } = -5;
    /// <summary>连续失败达到此阈值时注入提示词提醒LLM换策略（不禁用工具）</summary>
    public int WarningThreshold { get; set; } = 3;
    /// <summary>获取或设置评分下限。</summary>
    public int ScoreMin { get; set; } = -100;
    /// <summary>获取或设置评分上限。</summary>
    public int ScoreMax { get; set; } = 100;
    /// <summary>获取或设置每小时衰减率。</summary>
    public double DecayRatePerHour { get; set; } = 0.1;
    /// <summary>获取或设置衰减恢复评分。</summary>
    public int DecayRecoveryScore { get; set; } = 1;
}