
namespace Services.Todo;

[Register]
public sealed partial class TodoService : ITodoService
{
    [Inject] private readonly ITaskRuntime? _taskRuntime;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, TodoItem> _todos = new();

    public async Task<TodoServiceResult> WriteTodosAsync(List<TodoItemInput> todos, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todos);
        int createdCount = 0;
        int updatedCount = 0;
        int deletedCount = 0;

        var pendingTasks = new List<Task>();

        foreach (var todoInput in todos)
        {
            var todoId = todoInput.Id ?? $"todo_{Guid.NewGuid():N}";
            var todoPriority = todoInput.Priority ?? TodoPriorityConstants.Medium;
            var existingTodo = _todos.TryGetValue(todoId, out var existing) ? existing : null;

            if (todoInput.Status.Equals("deleted", StringComparison.OrdinalIgnoreCase))
            {
                if (existingTodo != null)
                {
                    _todos.TryRemove(todoId, out _);
                    deletedCount++;
                }

                if (_taskRuntime != null)
                {
                    var updateDeleted = new RuntimeTaskUpdate { Status = TaskExecutionStatus.Cancelled };
                    pendingTasks.Add(_taskRuntime.UpdateTaskAsync(todoId, updateDeleted, cancellationToken));
                }

                continue;
            }

            var todo = new TodoItem(
                todoId,
                todoInput.Content,
                todoInput.Status,
                todoPriority,
                todoInput.ParentId,
                todoInput.ActiveForm,
                existingTodo?.CreatedAt ?? _clock.GetUtcNow(),
                _clock.GetUtcNow());

            if (existingTodo == null)
            {
                createdCount++;

                if (_taskRuntime != null)
                {
                    var input = new RuntimeTaskInput
                    {
                        Description = todoInput.Content,
                        Priority = MapPriority(todoPriority),
                        GoalId = todoInput.ParentId,
                        IsLightweight = true,
                        IsDurable = false
                    };
                    pendingTasks.Add(_taskRuntime.CreateTaskAsync(input, cancellationToken));
                }
            }
            else
            {
                updatedCount++;

                if (_taskRuntime != null)
                {
                    var update = new RuntimeTaskUpdate
                    {
                        Description = todoInput.Content,
                        Status = MapStatus(todoInput.Status),
                        Priority = MapPriority(todoPriority)
                    };
                    pendingTasks.Add(_taskRuntime.UpdateTaskAsync(todoId, update, cancellationToken));
                }
            }

            _todos[todo.Id] = todo;
        }

        if (pendingTasks.Count > 0)
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }

        var allTodos = _todos.Values.ToList();
        RecordTodoMetrics("write", createdCount + updatedCount + deletedCount);
        return new TodoServiceResult(true, createdCount, updatedCount, deletedCount, allTodos);
    }

    public Task<TodoListResult> ListTodosAsync(string? status = null, string? priority = null, bool includeCompleted = false, CancellationToken cancellationToken = default)
    {
        var query = _todos.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(t => t.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(priority))
        {
            query = query.Where(t => t.Priority.Equals(priority, StringComparison.OrdinalIgnoreCase));
        }

        if (!includeCompleted)
        {
            query = query.Where(t => !t.Status.Equals(TodoStatusConstants.Completed, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.OrderBy(t => t.CreatedAt).ToList();
        return Task.FromResult(new TodoListResult(true, result));
    }

    public async Task<TodoItemResult> UpdateTodoAsync(string todoId, string? content = null, string? status = null, string? priority = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(todoId);
        if (!_todos.TryGetValue(todoId, out var existingTodo))
        {
            return new TodoItemResult(false, null, L.T(StringKey.VaultTodoNotFound));
        }

        var updatedTodo = existingTodo with
        {
            Content = content ?? existingTodo.Content,
            Status = status ?? existingTodo.Status,
            Priority = priority ?? existingTodo.Priority,
            UpdatedAt = _clock.GetUtcNow()
        };

        _todos[todoId] = updatedTodo;

        if (_taskRuntime != null)
        {
            var update = new RuntimeTaskUpdate
            {
                Description = content,
                Status = status != null ? MapStatus(status) : null,
                Priority = priority != null ? MapPriority(priority) : null
            };
            await _taskRuntime.UpdateTaskAsync(todoId, update, cancellationToken).ConfigureAwait(false);
        }

        return new TodoItemResult(true, updatedTodo);
    }

    public Task ClearTodosAsync(CancellationToken cancellationToken = default)
    {
        _todos.Clear();
        RecordTodoMetrics("clear", 0);
        return Task.CompletedTask;
    }

    private static TaskExecutionStatus MapStatus(string todoStatus)
    {
        var status = TodoStatusExtensions.FromValue(todoStatus);
        return status switch
        {
            TodoStatus.Pending => TaskExecutionStatus.Pending,
            TodoStatus.InProgress => TaskExecutionStatus.Running,
            TodoStatus.Completed => TaskExecutionStatus.Completed,
            TodoStatus.Cancelled => TaskExecutionStatus.Cancelled,
            _ => TaskExecutionStatus.Pending
        };
    }

    private static RuntimeTaskPriority MapPriority(string todoPriority)
    {
        var priority = TodoPriorityExtensions.FromValue(todoPriority);
        return priority switch
        {
            TodoPriority.High => RuntimeTaskPriority.Now,
            TodoPriority.Medium => RuntimeTaskPriority.Next,
            _ => RuntimeTaskPriority.Later
        };
    }

    private void RecordTodoMetrics(string operation, int count)
    {
        _telemetryService?.RecordCount("todo.operation.count", new Dictionary<string, string> { ["operation"] = operation }, "count", "Todo operation count");
        _telemetryService?.RecordHistogram("todo.operation.items", count, new Dictionary<string, string> { ["operation"] = operation }, "items", "Todo items affected");
    }
}
