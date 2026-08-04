
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 待办事项服务接口
/// </summary>
public interface ITodoService
{
    Task<TodoServiceResult> WriteTodosAsync(List<TodoItemInput> todos, CancellationToken cancellationToken = default);

    Task<TodoListResult> ListTodosAsync(string? status = null, string? priority = null, bool includeCompleted = false, CancellationToken cancellationToken = default);

    Task<OperationResult<TodoItem?>> UpdateTodoAsync(string todoId, string? content = null, string? status = null, string? priority = null, CancellationToken cancellationToken = default);

    Task ClearTodosAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoItem>> GetTopologicalOrderAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoItem>> GetReadyTodosAsync(CancellationToken cancellationToken = default);
}
