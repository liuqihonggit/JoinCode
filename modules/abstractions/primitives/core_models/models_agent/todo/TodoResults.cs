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
    string? ErrorMessage = null) {
    /// <summary>获取待办总数。</summary>
    public int TotalCount => Todos?.Count ?? 0;
    /// <summary>获取待处理数量。</summary>
    public int PendingCount => Todos?.Count(t => !t.Status.Equals(TodoStatusEnumConstants.Completed, StringComparison.OrdinalIgnoreCase)) ?? 0;
    /// <summary>获取已完成数量。</summary>
    public int CompletedCount => Todos?.Count(t => t.Status.Equals(TodoStatusEnumConstants.Completed, StringComparison.OrdinalIgnoreCase)) ?? 0;
}
