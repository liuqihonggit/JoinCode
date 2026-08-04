namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具健康记录 — 追踪单个工具的执行成功/失败/评分/熔断状态
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
/// 工具评分配置 — 控制奖惩幅度、熔断阈值、时间衰减率
/// </summary>
public sealed class ToolScoreConfig
{
    public int SuccessDelta { get; set; } = 1;
    public int FailDelta { get; set; } = -5;
    public int CircuitBreakerThreshold { get; set; } = 3;
    public int ScoreMin { get; set; } = -100;
    public int ScoreMax { get; set; } = 100;
    public double DecayRatePerHour { get; set; } = 0.1;
    public int DecayRecoveryScore { get; set; } = 1;
}
