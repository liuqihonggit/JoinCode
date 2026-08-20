namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具健康监控服务接口 — 追踪工具执行成功率、评分状态
/// 设计原则：永远不禁用工具，连续失败只注入提示词提醒LLM换策略
/// </summary>
public interface IToolHealthMonitor
{
    Task<ToolHealthRecord> RecordSuccessAsync(string toolName, CancellationToken ct = default);
    Task<ToolHealthRecord> RecordFailureAsync(string toolName, string? errorMessage, CancellationToken ct = default);
    Task<ToolHealthRecord?> GetRecordAsync(string toolName, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, ToolHealthRecord>> GetAllRecordsAsync(CancellationToken ct = default);
    Task ResetToolAsync(string toolName, CancellationToken ct = default);
    bool IsBlacklisted(string toolName);
    int GetPenalty(string toolName);
    int GetEffectiveScore(string toolName);
    void UpdateBlacklist(HashSet<string> newBlacklist);
    void UpdatePenalties(Dictionary<string, int> newPenalties);
}

/// <summary>
/// 工具健康记录 — 追踪单个工具的执行成功/失败/评分状态
/// </summary>
public sealed class ToolHealthRecord
{
    public required string ToolName { get; init; }
    public int Score { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime LastAdjusted { get; set; } = DateTime.UtcNow;
    public string? LastErrorMessage { get; set; }

    public double SuccessRate => SuccessCount + FailCount > 0
        ? (double)SuccessCount / (SuccessCount + FailCount) : 0.5;
}

/// <summary>
/// 工具健康监控扩展方法 — 提供错误追踪判断能力（合并自 ToolErrorTracker）
/// </summary>
public static class ToolHealthMonitorExtensions
{
    /// <summary>
    /// 判断工具是否应该自动修正 — 连续失败次数达到阈值
    /// </summary>
    public static async Task<bool> ShouldAutoFixAsync(
        this IToolHealthMonitor monitor,
        string toolName,
        int threshold = 3,
        CancellationToken ct = default)
    {
        var record = await monitor.GetRecordAsync(toolName, ct).ConfigureAwait(false);
        return record is not null && record.ConsecutiveFailures >= threshold;
    }

    /// <summary>
    /// 获取工具错误次数（= ConsecutiveFailures）
    /// </summary>
    public static async Task<int> GetErrorCountAsync(
        this IToolHealthMonitor monitor,
        string toolName,
        CancellationToken ct = default)
    {
        var record = await monitor.GetRecordAsync(toolName, ct).ConfigureAwait(false);
        return record?.ConsecutiveFailures ?? 0;
    }
}

/// <summary>
/// 工具评分配置 — 控制奖惩幅度、提示词阈值、时间衰减率
/// </summary>
public sealed class ToolScoreConfig
{
    public int SuccessDelta { get; set; } = 1;
    public int FailDelta { get; set; } = -5;
    /// <summary>连续失败达到此阈值时注入提示词提醒LLM换策略（不禁用工具）</summary>
    public int WarningThreshold { get; set; } = 3;
    public int ScoreMin { get; set; } = -100;
    public int ScoreMax { get; set; } = 100;
    public double DecayRatePerHour { get; set; } = 0.1;
    public int DecayRecoveryScore { get; set; } = 1;
}
