namespace Services.Todo;

[Register(typeof(ITodoService), ServiceLifetime.Singleton)]
public sealed partial class TodoService : ServiceEntity, ITodoService, IDisposable
{

    public TodoService(IClockService clock, ITaskRuntime? taskRuntime = null, ITelemetryService? telemetryService = null, IPersistencePipeline? persistencePipeline = null, IFileSystem? fs = null, ILogger<TodoService>? logger = null)
    {
        _clock = clock;
        _taskRuntime = taskRuntime;
        _telemetryService = telemetryService;
        _persistencePipeline = persistencePipeline;
        _fs = fs;
        _logger = logger;
    }
    private readonly ITaskRuntime? _taskRuntime;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly IPersistencePipeline? _persistencePipeline;
    private readonly IFileSystem? _fs;
    private readonly ILogger<TodoService>? _logger;
    private readonly ConcurrentDag<TodoItem> _todoDag = new();
    private int _todosLoaded;
    private static readonly string TodosSubDir = Path.Combine(AppDataConstants.AppDataFolder, "todo");
    private const string TodosFileName = "todos.json";

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
                            if (edgeResult.CyclePath.Count > 0)
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

        await SaveTodosAsync(cancellationToken).ConfigureAwait(false);

        return new TodoServiceResult(true, createdCount, updatedCount, deletedCount, allTodos);
    }

    public async Task<TodoListResult> ListTodosAsync(string? status = null, string? priority = null, bool includeCompleted = false, CancellationToken cancellationToken = default)
    {
        await EnsureTodosLoadedAsync(cancellationToken).ConfigureAwait(false);

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
        return new TodoListResult(true, result);
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

        await SaveTodosAsync(cancellationToken).ConfigureAwait(false);

        return OperationResult<TodoItem?>.Ok(updatedTodo);
    }

    public async Task ClearTodosAsync(CancellationToken cancellationToken = default)
    {
        _todoDag.Clear();
        RecordTodoMetrics("clear", 0);

        await SaveTodosAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodoItem>> GetTopologicalOrderAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTodosLoadedAsync(cancellationToken).ConfigureAwait(false);
        var sorted = _todoDag.TopologicalSort().Select(n => n.Payload).ToList();
        return sorted;
    }

    public async Task<IReadOnlyList<TodoItem>> GetReadyTodosAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTodosLoadedAsync(cancellationToken).ConfigureAwait(false);

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

        return ready;
    }

    private async Task SaveTodosAsync(CancellationToken ct)
    {
        if (_persistencePipeline is null) return;

        var snapshot = _todoDag.Nodes.Values.Select(n => n.Payload).ToList();
        var json = RelaxedJsonSerializer.Serialize(snapshot, TodoJsonContext.Default);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PersistRequest
        {
            Category = "todo",
            Directory = TodosSubDir,
            FileName = TodosFileName,
            Content = json,
            Completion = tcs,
        };
        await _persistencePipeline.EnqueueAsync(request, ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    private async Task EnsureTodosLoadedAsync(CancellationToken ct)
    {
        if (_fs is null || Interlocked.CompareExchange(ref _todosLoaded, 1, 0) != 0) return;

        try
        {
            var root = GitWorkspaceResolver.FindGitWorkspaceDir(null, _fs!);
            if (root is null) return;
            var path = Path.Combine(Path.Combine(root, TodosSubDir), TodosFileName);
            if (!_fs.FileExists(path)) return;
            var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var list = RelaxedJsonSerializer.Deserialize<List<TodoItem>>(json, TodoJsonContext.Default);
            if (list is null) return;
            foreach (var todo in list)
            {
                _todoDag.AddNode(new DagNode<TodoItem> { Id = todo.Id, Payload = todo });
            }
            foreach (var todo in list)
            {
                if (todo.DependsOn is { Count: > 0 })
                {
                    foreach (var depId in todo.DependsOn)
                    {
                        if (_todoDag.Nodes.ContainsKey(depId))
                        {
                            _todoDag.AddEdge(new DagEdge { FromId = depId, ToId = todo.Id, Label = "depends-on" });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("Todo 加载失败: {Message}", ex.Message);
        }
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

    protected override void OnDispose()
    {
        _todoDag.Dispose();
    }
}
