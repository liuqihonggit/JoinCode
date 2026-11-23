namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTaskUpdate {
    /// <summary>获取任务描述。</summary>
    public string? Description { get; init; }
    /// <summary>获取任务执行状态。</summary>
    public TaskExecutionStatus? Status { get; init; }
    /// <summary>获取任务优先级。</summary>
    public RuntimeTaskPriority? Priority { get; init; }
    /// <summary>获取代理标识。</summary>
    public string? AgentId { get; init; }
    /// <summary>获取任务结果。</summary>
    public string? Result { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? ErrorMessage { get; init; }
}