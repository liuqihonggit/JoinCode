
namespace Core.Scheduling;

/// <summary>
/// 任务文件元数据，用于JSON序列化存储
/// </summary>
public sealed record FileTaskMetadata
{
    /// <summary>
    /// 任务ID
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// 任务标题
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// 任务描述
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// 任务状态
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = TaskState.Pending.ToStateString();

    /// <summary>
    /// 优先级
    /// </summary>
    [JsonPropertyName("priority")]
    public string Priority { get; init; } = TodoPriorityConstants.Medium;

    /// <summary>
    /// 负责人
    /// </summary>
    [JsonPropertyName("assignee")]
    public string? Assignee { get; init; }

    /// <summary>
    /// 截止日期
    /// </summary>
    [JsonPropertyName("dueDate")]
    public DateTime? DueDate { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 标签列表
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = new();

    /// <summary>
    /// 依赖的任务ID列表
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; init; } = new();

    /// <summary>
    /// 阻塞的任务ID列表
    /// </summary>
    [JsonPropertyName("blockedBy")]
    public List<string> BlockedBy { get; init; } = new();

    /// <summary>
    /// 从 TaskItem 创建 FileTaskMetadata
    /// </summary>
    public static FileTaskMetadata FromTaskItem(TaskItem item)
    {
        return new FileTaskMetadata
        {
            Id = item.Id,
            Title = item.Title,
            Description = item.Description,
            Status = item.Status,
            Priority = item.Priority.ToValue(),
            Assignee = item.Assignee,
            DueDate = item.DueDate,
            CreatedAt = item.CreatedAt,
            Tags = item.Tags?.ToList() ?? new List<string>()
        };
    }

    /// <summary>
    /// 转换为 TaskItem
    /// </summary>
    public TaskItem ToTaskItem()
    {
        return new TaskItem
        {
            Id = Id,
            Title = Title,
            Description = Description,
            Status = Status,
            Priority = TodoPriorityExtensions.FromValue(Priority) ?? TodoPriority.Medium,
            Assignee = Assignee,
            DueDate = DueDate,
            CreatedAt = CreatedAt,
            Tags = Tags?.ToList() ?? new List<string>()
        };
    }

    /// <summary>
    /// 序列化为 JSON
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, SchedulingIndentedJsonContext.Default.FileTaskMetadata);
    }

    /// <summary>
    /// 从 JSON 反序列化
    /// </summary>
    public static FileTaskMetadata? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize(json, SchedulingJsonContext.Default.FileTaskMetadata);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 任务状态转换帮助类 — 委托给 TaskStatusExtensions（源码生成器自动生成）
/// </summary>
public static class TaskStateConverter
{
    /// <summary>
    /// 将 TaskState 枚举转换为小写字符串
    /// </summary>
    public static string ToStateString(this TaskState state) => JoinCode.Abstractions.Models.Task.TaskStatusExtensions.ToValue((JoinCode.Abstractions.Models.Task.TaskStatus)state) ?? JoinCode.Abstractions.Models.Task.TaskStatusConstants.Pending;

    /// <summary>
    /// 将状态字符串转换为 TaskState 枚举
    /// </summary>
    public static TaskState ToTaskState(this string? state) => (TaskState)(JoinCode.Abstractions.Models.Task.TaskStatusExtensions.FromValue(state) ?? JoinCode.Abstractions.Models.Task.TaskStatus.Pending);
}
