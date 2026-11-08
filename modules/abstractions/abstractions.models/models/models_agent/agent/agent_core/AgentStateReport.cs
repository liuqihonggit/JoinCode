namespace JoinCode.Abstractions.Models.Agent;

public sealed class AgentStateInfo {
    /// <summary>获取智能体标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取任务描述。</summary>
    public required string Task { get; init; }
    /// <summary>获取当前执行状态。</summary>
    public required TaskExecutionStatus CurrentState { get; init; }
    /// <summary>获取开始时间。</summary>
    public DateTime? StartedAt { get; init; }
    /// <summary>获取完成时间。</summary>
    public DateTime? CompletedAt { get; init; }
    /// <summary>获取执行耗时（毫秒）。</summary>
    public long? ExecutionTimeMs { get; init; }
    /// <summary>获取子智能体选项。</summary>
    public SubAgentOptions? Options { get; init; }
    /// <summary>获取智能体进度。</summary>
    public JoinCode.Abstractions.Interfaces.AgentProgress? Progress { get; init; }
}

public sealed class AgentStateReport {
    /// <summary>获取智能体总数。</summary>
    public int TotalAgents { get; init; }
    /// <summary>获取待处理数量。</summary>
    public int PendingCount { get; init; }
    /// <summary>获取运行中数量。</summary>
    public int RunningCount { get; init; }
    /// <summary>获取已暂停数量。</summary>
    public int PausedCount { get; init; }
    /// <summary>获取已完成数量。</summary>
    public int CompletedCount { get; init; }
    /// <summary>获取失败数量。</summary>
    public int FailedCount { get; init; }
    /// <summary>获取已取消数量。</summary>
    public int CancelledCount { get; init; }
    /// <summary>获取智能体状态信息列表。</summary>
    public List<AgentStateInfo> Agents { get; init; } = new();
}
