namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 统一任务实体 — 派生自 Entity，与 Agent 同套路
/// ObjectId + 任务描述 + 创建时间 + 独立注册器 + 静态属性暴露
/// 替代 TaskItem record（TaskItem 仍保留为 ITaskService 的 DTO，AgentTask 是运行时实体）
/// </summary>
public sealed class AgentTask : Entity
{
    public string Title { get; }
    public string? Description { get; init; }
    public TaskType Type { get; init; }
    public TaskExecutionStatus Status { get; set; }
    public ObjectId? AssigneeObjectId { get; init; }
    public ObjectId? ParentTaskObjectId { get; init; }
    public TodoPriority Priority { get; init; }
    public string? Assignee { get; init; }
    public DateTime? DueDate { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 全局唯一 Task 注册器 — 静态属性暴露，无需DI
    /// </summary>
    public static TaskRegistry Registry { get; } = new();

    public AgentTask(
        string title,
        TaskType type = TaskType.LocalAgent,
        TodoPriority priority = TodoPriority.Medium,
        ObjectId? assigneeObjectId = default,
        ObjectId? parentTaskObjectId = default,
        string? description = null,
        string? assignee = null,
        DateTime? dueDate = null,
        IReadOnlyList<string>? tags = null,
        string? id = null)
        : base(ObjectType.Task, id)
    {
        Title = title;
        Type = type;
        Priority = priority;
        AssigneeObjectId = assigneeObjectId;
        ParentTaskObjectId = parentTaskObjectId;
        Description = description;
        Assignee = assignee;
        DueDate = dueDate;
        Tags = tags ?? Array.Empty<string>();
        Status = TaskExecutionStatus.Pending;

        Registry.Add(ObjectId, this);
    }

    /// <summary>
    /// 惰性释放 — 持久化服务确认数据全部写入后才调用
    /// </summary>
    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }

    /// <summary>
    /// 转换为 TaskItem DTO（供 ITaskService 持久化层使用）
    /// </summary>
    public TaskItem ToTaskItem() => new()
    {
        Id = Id,
        Title = Title,
        Description = Description,
        Status = Status.ToValue(),
        Priority = Priority,
        Assignee = Assignee,
        DueDate = DueDate,
        CreatedAt = CreatedAt,
        Tags = Tags
    };

    /// <summary>
    /// 从 TaskItem DTO 创建 AgentTask 实体（反持久化）
    /// </summary>
    public static AgentTask FromTaskItem(TaskItem item) => new(
        title: item.Title,
        priority: item.Priority,
        description: item.Description,
        assignee: item.Assignee,
        dueDate: item.DueDate,
        tags: item.Tags,
        id: item.Id)
    {
        Status = TaskExecutionStatusExtensions.FromValue(item.Status) ?? TaskExecutionStatus.Pending
    };
}
