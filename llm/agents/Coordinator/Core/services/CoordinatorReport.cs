namespace Core.Agents.Coordinator;

/// <summary>
/// 协调器报告
/// </summary>
public sealed class CoordinatorReport
{
    /// <summary>Agent 总数</summary>
    public int TotalAgents { get; init; }
    /// <summary>待执行 Agent 数量</summary>
    public int PendingCount { get; init; }
    /// <summary>运行中 Agent 数量</summary>
    public int RunningCount { get; init; }
    /// <summary>已暂停 Agent 数量</summary>
    public int PausedCount { get; init; }
    /// <summary>已完成 Agent 数量</summary>
    public int CompletedCount { get; init; }
    /// <summary>失败 Agent 数量</summary>
    public int FailedCount { get; init; }
    /// <summary>已取消 Agent 数量</summary>
    public int CancelledCount { get; init; }
    /// <summary>Agent 信息列表</summary>
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
    /// <summary>Agent 总数</summary>
    public int TotalAgents { get; init; }
    /// <summary>成功 Agent 数量</summary>
    public int SuccessfulAgents { get; init; }
    /// <summary>失败 Agent 数量</summary>
    public int FailedAgents { get; init; }
    /// <summary>已取消 Agent 数量</summary>
    public int CancelledAgents { get; init; }
    /// <summary>总重试次数</summary>
    public int TotalRetries { get; init; }
    /// <summary>平均执行时间（毫秒）</summary>
    public long? AverageExecutionTimeMs { get; init; }
    /// <summary>并行执行次数</summary>
    public int ParallelExecutions { get; init; }
    /// <summary>串行执行次数</summary>
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
    /// <summary>Agent 标识</summary>
    public required string Id { get; init; }
    /// <summary>任务描述</summary>
    public required string Task { get; init; }
    /// <summary>当前执行状态</summary>
    public required TaskExecutionStatus State { get; init; }
    /// <summary>执行耗时（毫秒）</summary>
    public long? ExecutionTimeMs { get; init; }
}

