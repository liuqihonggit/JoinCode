
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 待办事项服务接口
/// </summary>
public interface ITodoService
{
    /// <summary>
    /// 写入待办事项（创建或更新）
    /// </summary>
    Task<TodoServiceResult> WriteTodosAsync(List<TodoItemInput> todos, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出待办事项
    /// </summary>
    Task<TodoListResult> ListTodosAsync(string? status = null, string? priority = null, bool includeCompleted = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新单个待办事项
    /// </summary>
    Task<TodoItemResult> UpdateTodoAsync(string todoId, string? content = null, string? status = null, string? priority = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除所有待办事项（all-done→clear 行为）
    /// </summary>
    Task ClearTodosAsync(CancellationToken cancellationToken = default);
}
