namespace JoinCode.Abstractions.Models.Runtime;

/// <summary>运行时任务输入。</summary>
public sealed record RuntimeTaskInput {
    /// <summary>获取任务描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取或设置任务优先级。</summary>
    public RuntimeTaskPriority Priority { get; init; } = RuntimeTaskPriority.Later;
    /// <summary>获取或设置目标 ID。</summary>
    public string? GoalId { get; init; }
    /// <summary>获取或设置代理 ID。</summary>
    public string? AgentId { get; init; }
    /// <summary>获取或设置依赖任务 ID 列表。</summary>
    public List<string>? Dependencies { get; init; }
    /// <summary>获取或设置 Cron 表达式。</summary>
    public string? CronExpression { get; init; }
    /// <summary>获取或设置是否为持久任务。</summary>
    public bool IsDurable { get; init; }
    /// <summary>获取或设置是否为轻量任务。</summary>
    public bool IsLightweight { get; init; }
    /// <summary>获取或设置最大重试次数。</summary>
    public int MaxRetries { get; init; } = 2;
}
