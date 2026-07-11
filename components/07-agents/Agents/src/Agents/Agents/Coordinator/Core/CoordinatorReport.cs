namespace Core.Agents.Coordinator;

/// <summary>
/// 协调器报告
/// </summary>
public sealed class CoordinatorReport
{
    public int TotalAgents { get; init; }
    public int PendingCount { get; init; }
    public int RunningCount { get; init; }
    public int PausedCount { get; init; }
    public int CompletedCount { get; init; }
    public int FailedCount { get; init; }
    public int CancelledCount { get; init; }
    public List<AgentInfo> Agents { get; init; } = new();

    /// <summary>
    /// 平均执行时间（毫秒）
    /// </summary>
    public long? AverageExecutionTimeMs { get; init; }

    /// <summary>
    /// 总重试次数
    /// </summary>
    public int TotalRetries { get; init; }

    /// <summary>
    /// 有重试的Agent数量
    /// </summary>
    public int AgentsWithRetries { get; init; }
}

/// <summary>
/// 备用执行结果
/// </summary>
public sealed class FallbackExecutionResult
{
    /// <summary>
    /// 所有执行结果（包括主Agent和备用Agent）
    /// </summary>
    public required IReadOnlyList<SubAgentResult> AllResults { get; init; }

    /// <summary>
    /// 成功的结果（如果所有都失败则为null）
    /// </summary>
    public SubAgentResult? SuccessfulResult { get; init; }

    /// <summary>
    /// 成功的Agent ID（如果所有都失败则为null）
    /// </summary>
    public string? SuccessAgentId { get; init; }

    /// <summary>
    /// 尝试次数
    /// </summary>
    public int AttemptCount { get; init; }

    /// <summary>
    /// 是否有成功的执行
    /// </summary>
    public bool IsSuccess => SuccessfulResult != null;
}

/// <summary>
/// 执行统计信息
/// </summary>
public sealed class ExecutionStatistics
{
    public int TotalAgents { get; init; }
    public int SuccessfulAgents { get; init; }
    public int FailedAgents { get; init; }
    public int CancelledAgents { get; init; }
    public int TotalRetries { get; init; }
    public long? AverageExecutionTimeMs { get; init; }
    public int ParallelExecutions { get; init; }
    public int SequentialExecutions { get; init; }

    /// <summary>
    /// 成功率（0-1）
    /// </summary>
    public double SuccessRate => TotalAgents > 0 ? (double)SuccessfulAgents / TotalAgents : 0;
}

/// <summary>
/// Agent信息
/// </summary>
public sealed class AgentInfo
{
    public required string Id { get; init; }
    public required string Task { get; init; }
    public required TaskExecutionStatus State { get; init; }
    public long? ExecutionTimeMs { get; init; }
}

