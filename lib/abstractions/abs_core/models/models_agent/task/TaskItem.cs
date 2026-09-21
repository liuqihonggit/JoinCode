namespace JoinCode.Abstractions.Models.Task;

/// <summary>
/// 任务项
/// </summary>
public sealed record TaskItem {
    /// <summary>获取任务标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取任务标题。</summary>
    public required string Title { get; init; }
    /// <summary>获取任务描述。</summary>
    public string? Description { get; init; }
    /// <summary>获取任务状态。</summary>
    public string Status { get; init; } = TaskExecutionStatusEnumConstants.Pending;
    /// <summary>获取任务优先级。</summary>
    public TodoPriority Priority { get; init; } = TodoPriority.Medium;
    /// <summary>获取指派人。</summary>
    public string? Assignee { get; init; }
    /// <summary>获取截止日期。</summary>
    public DateTime? DueDate { get; init; }
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取标签列表。</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 任务依赖类型
/// </summary>
public enum TaskDependencyType {
    /// <summary>
    /// 阻塞关系 - 依赖任务完成后才能执行当前任务
    /// </summary>
    [EnumValue("blocks")] Blocks,

    /// <summary>
    /// 软依赖 - 依赖任务完成后当前任务才能标记为完成，但可以并行执行
    /// </summary>
    [EnumValue("soft")] Soft,

    /// <summary>
    /// 子任务 - 当前任务是依赖任务的子任务
    /// </summary>
    [EnumValue("subtask")] Subtask
}

/// <summary>
/// 任务依赖关系
/// </summary>
public sealed record TaskDependency {
    /// <summary>
    /// 任务ID
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// 依赖的任务ID
    /// </summary>
    public required string DependsOnTaskId { get; init; }

    /// <summary>
    /// 依赖类型
    /// </summary>
    public TaskDependencyType DependencyType { get; init; } = TaskDependencyType.Blocks;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 正在运行的任务信息
/// </summary>
public sealed record RunningTaskInfo {
    /// <summary>获取任务标识。</summary>
    public required string Id { get; init; }
    /// <summary>获取任务描述。</summary>
    public required string Description { get; init; }
    /// <summary>获取任务状态。</summary>
    public string Status { get; init; } = TaskExecutionStatusEnumConstants.Running;
    /// <summary>获取开始时间。</summary>
    public DateTime? StartedAt { get; init; }
}