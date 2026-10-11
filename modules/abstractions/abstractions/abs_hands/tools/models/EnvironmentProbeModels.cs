namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 环境探测报告 — 描述运行环境的能力和评分
/// </summary>
public sealed class EnvironmentReport {
    /// <summary>获取探测时间。</summary>
    public DateTime ProbeTime { get; init; } = DateTime.UtcNow;
    /// <summary>获取组件评分列表。</summary>
    public List<ComponentScore> Components { get; init; } = [];
    /// <summary>获取推荐的 Shell。</summary>
    public string RecommendedShell { get; init; } = string.Empty;
}

/// <summary>
/// 组件评分 — 单个环境组件的安装状态和评分
/// </summary>
public sealed record ComponentScore {
    /// <summary>获取组件标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取组件名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取版本。</summary>
    public string? Version { get; init; }
    /// <summary>获取是否已安装。</summary>
    public bool IsInstalled { get; init; }
    /// <summary>获取评分。</summary>
    public int Score { get; init; }
    /// <summary>获取路径。</summary>
    public string? Path { get; init; }
}

/// <summary>
/// 执行器评分 — 多执行器综合评分模型，供Shell工具选择最优执行环境
/// </summary>
public sealed record ExecutorScore {
    /// <summary>获取执行器标识。</summary>
    public required string ExecutorId { get; init; }
    /// <summary>获取评分。</summary>
    public int Score { get; init; }
    /// <summary>获取失败次数。</summary>
    public int FailCount { get; init; }
    /// <summary>获取成功次数。</summary>
    public int SuccessCount { get; init; }
    /// <summary>获取成功率。</summary>
    public double SuccessRate => SuccessCount + FailCount > 0
        ? (double)SuccessCount / (SuccessCount + FailCount) : 0.5;
    /// <summary>获取评分原因。</summary>
    public string Reason { get; init; } = string.Empty;
}