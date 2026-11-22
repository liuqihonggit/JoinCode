namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTask {
    /// <summary>获取任务标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取或设置任务描述。</summary>
    public required string Description { get; set; }
    /// <summary>获取或设置任务执行状态。</summary>
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;
    /// <summary>获取或设置任务优先级。</summary>
    public RuntimeTaskPriority Priority { get; set; } = RuntimeTaskPriority.Later;
    /// <summary>获取目标标识。</summary>
    public string? GoalId { get; init; }
    /// <summary>获取或设置代理标识。</summary>
    public string? AgentId { get; set; }
    /// <summary>获取依赖任务列表。</summary>
    public List<string> Dependencies { get; init; } = [];
    /// <summary>获取 Cron 表达式。</summary>
    public string? CronExpression { get; init; }
    /// <summary>获取是否为持久化任务。</summary>
    public bool IsDurable { get; init; }
    /// <summary>获取是否为轻量级任务。</summary>
    public bool IsLightweight { get; init; }
    /// <summary>获取或设置重试次数。</summary>
    public int RetryCount { get; set; }
    /// <summary>获取最大重试次数。</summary>
    public int MaxRetries { get; init; } = 2;
    /// <summary>获取或设置任务结果。</summary>
    public string? Result { get; set; }
    /// <summary>获取或设置错误信息。</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取或设置开始时间。</summary>
    public DateTime? StartedAt { get; set; }
    /// <summary>获取或设置完成时间。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>获取是否可以重试。</summary>
    public bool CanRetry => RetryCount < MaxRetries && Status == TaskExecutionStatus.Failed;

    /// <summary>获取任务持续时间。</summary>
    public TimeSpan? Duration => StartedAt.HasValue && CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;
}