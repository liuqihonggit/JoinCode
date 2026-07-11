namespace JoinCode.Abstractions.Models.Task;

/// <summary>
/// 任务操作结果
/// </summary>
public sealed record TaskOperationResult(
    bool Success,
    TaskItem? Task = null,
    string? ErrorMessage = null);

/// <summary>
/// 任务列表结果
/// </summary>
public sealed record TaskListResult(
    bool Success,
    List<TaskItem> Tasks,
    int TotalCount,
    string? ErrorMessage = null);
