namespace JoinCode.Abstractions.Models.Task;

/// <summary>
/// 更新任务请求 — 封装 ITaskService.UpdateTaskAsync 的业务参数
/// </summary>
public sealed record UpdateTaskRequest
{
    /// <summary>
    /// 任务ID
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// 新标题（null 表示不更新）
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// 新描述（null 表示不更新）
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 新状态（null 表示不更新）
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// 新负责人（null 表示不更新）
    /// </summary>
    public string? Assignee { get; init; }

    /// <summary>
    /// 新截止日期（null 表示不更新）
    /// </summary>
    public DateTime? DueDate { get; init; }

    /// <summary>
    /// 新优先级（null 表示不更新）
    /// </summary>
    public string? Priority { get; init; }

    /// <summary>
    /// 新标签列表（null 表示不更新）
    /// </summary>
    public List<string>? Tags { get; init; }
}
