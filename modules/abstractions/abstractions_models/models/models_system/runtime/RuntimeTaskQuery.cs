namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTaskQuery {
    /// <summary>获取任务状态过滤条件。</summary>
    public TaskExecutionStatus? Status { get; init; }
    /// <summary>获取目标标识过滤条件。</summary>
    public string? GoalId { get; init; }
    /// <summary>获取代理标识过滤条件。</summary>
    public string? AgentId { get; init; }
    /// <summary>获取优先级过滤条件。</summary>
    public RuntimeTaskPriority? Priority { get; init; }
    /// <summary>获取是否包含已完成任务。</summary>
    public bool IncludeCompleted { get; init; }
    /// <summary>获取结果数量限制。</summary>
    public int Limit { get; init; } = 50;
    /// <summary>获取结果偏移量。</summary>
    public int Offset { get; init; }
}