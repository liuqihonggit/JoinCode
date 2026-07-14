namespace JoinCode.Abstractions.Models.Task;

/// <summary>
/// 任务列表结果
/// </summary>
public sealed record TaskListResult(
    bool Success,
    List<TaskItem> Tasks,
    int TotalCount,
    string? ErrorMessage = null);
