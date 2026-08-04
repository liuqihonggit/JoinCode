using Structura.Dag;

namespace Services.Todo;

[Register]
public sealed partial class TodoService : ITodoService, IDisposable
{
    [Inject] private readonly ITaskRuntime? _taskRuntime;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly IClockService _clock;
    private readonly ConcurrentDag<TodoItem> _todoDag = new();

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
            var existingNode = _todoDag.Nodes.TryGetValue(todoId, out var n) ? n : null;
            var existingTodo = existingNode?.Payload;

            if (todoInput.Status.Equals("deleted", StringComparison.OrdinalIgnoreCase))
            {
                if (existingTodo != null)
                {
                    _todoDag.RemoveNode(todoId);
                    deletedCount++;

                    if (_taskRuntime != null)
                    {
                        pendingTasks.Add(_taskRuntime.UpdateTaskAsync(todoId, new RuntimeTaskUpdate { Status = TaskExecutionStatus.Cancelled }, cancellationToken));
                    }
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
                _clock.GetUtcNow(),
                todoInput.DependsOn,
                todoInput.OwnedFiles);

            if (existingTodo == null)
            {
                createdCount++;
                var addResult = _todoDag.AddNode(new DagNode<TodoItem> { Id = todoId, Payload = todo });
                if (addResult.Success && todo.DependsOn is { Count: > 0 })
                {
                    foreach (var depId in todo.DependsOn)
                    {
                        if (_todoDag.Nodes.ContainsKey(depId))
                        {
                            var edgeResult = _todoDag.AddEdge(new DagEdge { FromId = depId, ToId = todoId, Label = "depends-on" });
                            if (edgeResult.CyclePath is not null)
                            {
                                _todoDag.RemoveNode(todoId);
                                createdCount--;
                                deletedCount++;
                                goto NextItem;
                            }
                        }
                    }
                }

                if (_taskRuntime != null)
                {
                    pendingTasks.Add(_taskRuntime.CreateTaskAsync(new RuntimeTaskInput
                    {
                        Description = todoInput.Content,
                        Priority = MapPriority(todoPriority),
                        GoalId = todoInput.ParentId,
                        IsLightweight = true,
                        IsDurable = false
                    }, cancellationToken));
                }
            }
            else
            {
                updatedCount++;
                _todoDag.RemoveNode(todoId);
                _todoDag.AddNode(new DagNode<TodoItem> { Id = todoId, Payload = todo });
                if (todo.DependsOn is { Count: > 0 })
                {
                    foreach (var depId in todo.DependsOn)
                    {
                        if (_todoDag.Nodes.ContainsKey(depId))
                        {
                            _todoDag.AddEdge(new DagEdge { FromId = depId, ToId = todoId, Label = "depends-on" });
                        }
                    }
                }

                if (_taskRuntime != null)
                {
                    pendingTasks.Add(_taskRuntime.UpdateTaskAsync(todoId, new RuntimeTaskUpdate
                    {
                        Description = todoInput.Content,
                        Status = MapStatus(todoInput.Status),
                        Priority = MapPriority(todoPriority)
                    }, cancellationToken));
                }
            }

        NextItem:;
        }

        if (pendingTasks.Count > 0)
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }

        var allTodos = _todoDag.Nodes.Values.Select(n => n.Payload).ToList();
        RecordTodoMetrics("write", createdCount + updatedCount + deletedCount);
        return new TodoServiceResult(true, createdCount, updatedCount, deletedCount, allTodos);
    }

    public Task<TodoListResult> ListTodosAsync(string? status = null, string? priority = null, bool includeCompleted = false, CancellationToken cancellationToken = default)
    {
        var query = _todoDag.Nodes.Values.Select(n => n.Payload).AsEnumerable();

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

    public async Task<OperationResult<TodoItem?>> UpdateTodoAsync(string todoId, string? content = null, string? status = null, string? priority = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(todoId);
        if (!_todoDag.Nodes.TryGetValue(todoId, out var existingNode))
        {
            return OperationResult<TodoItem?>.Fail(L.T(StringKey.VaultTodoNotFound));
        }

        var existingTodo = existingNode.Payload;
        var updatedTodo = existingTodo with
        {
            Content = content ?? existingTodo.Content,
            Status = status ?? existingTodo.Status,
            Priority = priority ?? existingTodo.Priority,
            UpdatedAt = _clock.GetUtcNow()
        };

        _todoDag.RemoveNode(todoId);
        _todoDag.AddNode(new DagNode<TodoItem> { Id = todoId, Payload = updatedTodo });
        if (updatedTodo.DependsOn is { Count: > 0 })
        {
            foreach (var depId in updatedTodo.DependsOn)
            {
                if (_todoDag.Nodes.ContainsKey(depId))
                {
                    _todoDag.AddEdge(new DagEdge { FromId = depId, ToId = todoId, Label = "depends-on" });
                }
            }
        }

        if (_taskRuntime != null)
        {
            await _taskRuntime.UpdateTaskAsync(todoId, new RuntimeTaskUpdate
            {
                Description = content,
                Status = status != null ? MapStatus(status) : null,
                Priority = priority != null ? MapPriority(priority) : null
            }, cancellationToken).ConfigureAwait(false);
        }

        return OperationResult<TodoItem?>.Ok(updatedTodo);
    }

    public Task ClearTodosAsync(CancellationToken cancellationToken = default)
    {
        _todoDag.Clear();
        RecordTodoMetrics("clear", 0);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TodoItem>> GetTopologicalOrderAsync(CancellationToken cancellationToken = default)
    {
        var sorted = _todoDag.TopologicalSort().Select(n => n.Payload).ToList();
        return Task.FromResult<IReadOnlyList<TodoItem>>(sorted);
    }

    public Task<IReadOnlyList<TodoItem>> GetReadyTodosAsync(CancellationToken cancellationToken = default)
    {
        var completedIds = _todoDag.Nodes.Values
            .Where(n => n.Payload.Status.Equals(TodoStatusConstants.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        var ready = _todoDag.Nodes.Values
            .Where(n => !n.Payload.Status.Equals(TodoStatusConstants.Completed, StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Payload.Status.Equals(TodoStatusConstants.Cancelled, StringComparison.OrdinalIgnoreCase))
            .Where(n =>
            {
                var deps = n.Payload.DependsOn;
                if (deps is null || deps.Count == 0) return true;
                return deps.All(d => completedIds.Contains(d));
            })
            .Select(n => n.Payload)
            .ToList();

        return Task.FromResult<IReadOnlyList<TodoItem>>(ready);
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

    public void Dispose()
    {
        _todoDag.Dispose();
    }
}
