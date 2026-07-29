namespace JoinCode.Abstractions.Models.Todo;

/// <summary>
/// 待办服务结果
/// </summary>
public sealed record TodoServiceResult(
    bool Success,
    int CreatedCount,
    int UpdatedCount,
    int DeletedCount,
    List<TodoItem> CurrentTodos,
    string? ErrorMessage = null);

/// <summary>
/// 待办列表结果
/// </summary>
public sealed record TodoListResult(
    bool Success,
    List<TodoItem> Todos,
    string? ErrorMessage = null)
{
    public int TotalCount => Todos?.Count ?? 0;
    public int PendingCount => Todos?.Count(t => !t.Status.Equals(TodoStatusConstants.Completed, StringComparison.OrdinalIgnoreCase)) ?? 0;
    public int CompletedCount => Todos?.Count(t => t.Status.Equals(TodoStatusConstants.Completed, StringComparison.OrdinalIgnoreCase)) ?? 0;
}

