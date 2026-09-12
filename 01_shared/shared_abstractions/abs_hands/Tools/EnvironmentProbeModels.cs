namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 环境探测报告 — 描述运行环境的能力和评分
/// </summary>
public sealed class EnvironmentReport
{
    public DateTime ProbeTime { get; init; } = DateTime.UtcNow;
    public List<ComponentScore> Components { get; init; } = [];
    public string RecommendedShell { get; init; } = string.Empty;
}

/// <summary>
/// 组件评分 — 单个环境组件的安装状态和评分
/// </summary>
public sealed record ComponentScore
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public bool IsInstalled { get; init; }
    public int Score { get; init; }
    public string? Path { get; init; }
}

/// <summary>
/// 执行器评分 — 多执行器综合评分模型，供Shell工具选择最优执行环境
/// </summary>
public sealed record ExecutorScore
{
    public required string ExecutorId { get; init; }
    public int Score { get; init; }
    public int FailCount { get; init; }
    public int SuccessCount { get; init; }
    public double SuccessRate => SuccessCount + FailCount > 0
        ? (double)SuccessCount / (SuccessCount + FailCount) : 0.5;
    public string Reason { get; init; } = string.Empty;
}
