
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 待办事项服务接口
/// </summary>
public interface ITodoService {
    /// <summary>异步写入待办事项列表。</summary>
    Task<TodoServiceResult> WriteTodosAsync(List<TodoItemInput> todos, CancellationToken cancellationToken = default);

    /// <summary>异步列出待办事项。</summary>
    Task<TodoListResult> ListTodosAsync(string? status = null, string? priority = null, bool includeCompleted = false, CancellationToken cancellationToken = default);

    /// <summary>异步更新待办事项。</summary>
    Task<OperationResult<TodoItem?>> UpdateTodoAsync(string todoId, string? content = null, string? status = null, string? priority = null, CancellationToken cancellationToken = default);

    /// <summary>异步清空所有待办事项。</summary>
    Task ClearTodosAsync(CancellationToken cancellationToken = default);

    /// <summary>异步获取拓扑排序的待办事项列表。</summary>
    Task<IReadOnlyList<TodoItem>> GetTopologicalOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>异步获取就绪状态的待办事项列表。</summary>
    Task<IReadOnlyList<TodoItem>> GetReadyTodosAsync(CancellationToken cancellationToken = default);
}