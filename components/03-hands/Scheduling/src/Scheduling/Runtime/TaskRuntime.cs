namespace Core.Scheduling.Runtime;

using JoinCode.Abstractions.Attributes;

[Register]
public sealed partial class TaskRuntime : ITaskRuntime, IDisposable
{
    private readonly ConcurrentDictionary<string, RuntimeTask> _tasks = new();
    private readonly ConcurrentDag<string> _dag = new();
    [Inject] private readonly ILogger<TaskRuntime>? _logger;
    private readonly IClockService _clock;
    private readonly TaskRuntimeDeps _deps;
    private readonly SemaphoreSlim _persistLock;
    private int _taskCounter;

    public TaskRuntime(
        TaskRuntimeDeps? deps = null,
        ILogger<TaskRuntime>? logger = null,
        IClockService? clock = null)
    {
        _deps = deps ?? new TaskRuntimeDeps();
        if (string.IsNullOrEmpty(_deps.PersistenceDirectory))
        {
            _deps = _deps with { PersistenceDirectory = Path.Combine(AppContext.BaseDirectory, "runtime-tasks") };
        }
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _persistLock = new SemaphoreSlim(1, 1);
    }

    public Task<RuntimeTaskResult> CreateTaskAsync(RuntimeTaskInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var taskId = GenerateTaskId();
        var task = new RuntimeTask
        {
            Id = taskId,
            Description = input.Description,
            Priority = input.Priority,
            GoalId = input.GoalId,
            AgentId = input.AgentId,
            Dependencies = input.Dependencies ?? [],
            CronExpression = input.CronExpression,
            IsDurable = input.IsDurable,
            IsLightweight = input.IsLightweight,
            MaxRetries = input.MaxRetries
        };

        if (input.Dependencies is { Count: > 0 })
        {
            _dag.AddNode(new DagNode<string> { Id = taskId, Payload = taskId });
            foreach (var depId in input.Dependencies)
            {
                if (!_dag.Nodes.ContainsKey(depId))
                    _dag.AddNode(new DagNode<string> { Id = depId, Payload = depId });
                _dag.AddEdge(new DagEdge { FromId = depId, ToId = taskId, Label = "DEPENDS_ON" });
            }
        }
        else
        {
            _dag.AddNode(new DagNode<string> { Id = taskId, Payload = taskId });
        }

        _tasks[taskId] = task;

        _logger?.LogInformation(L.T(StringKey.RuntimeCreateTaskLog), taskId, input.Description);

        return Task.FromResult(RuntimeTaskResult.Ok(task));
    }

    public Task<RuntimeTaskResult> UpdateTaskAsync(string taskId, RuntimeTaskUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!_tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(RuntimeTaskResult.Fail(L.T(StringKey.RuntimeTaskNotExist, taskId)));
        }

        if (update.Description is not null) task.Description = update.Description;
        if (update.Status is not null) task.Status = update.Status.Value;
        if (update.Priority is not null) task.Priority = update.Priority.Value;
        if (update.AgentId is not null) task.AgentId = update.AgentId;
        if (update.Result is not null) task.Result = update.Result;
        if (update.ErrorMessage is not null) task.ErrorMessage = update.ErrorMessage;

        if (update.Status == TaskExecutionStatus.Running && task.StartedAt is null)
        {
            task.StartedAt = _clock.GetUtcNow();
        }

        if ((update.Status == TaskExecutionStatus.Completed ||
             update.Status == TaskExecutionStatus.Failed ||
             update.Status == TaskExecutionStatus.Cancelled) && task.CompletedAt is null)
        {
            task.CompletedAt = _clock.GetUtcNow();
        }

        _logger?.LogDebug(L.T(StringKey.RuntimeUpdateTaskLog), taskId, task.Status);

        return Task.FromResult(RuntimeTaskResult.Ok(task));
    }

    public Task<RuntimeTaskListResult> ListTasksAsync(RuntimeTaskQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = _tasks.Values.AsEnumerable();

        if (query.Status is not null)
        {
            filtered = filtered.Where(t => t.Status == query.Status);
        }

        if (query.GoalId is not null)
        {
            filtered = filtered.Where(t => t.GoalId == query.GoalId);
        }

        if (query.AgentId is not null)
        {
            filtered = filtered.Where(t => t.AgentId == query.AgentId);
        }

        if (query.Priority is not null)
        {
            filtered = filtered.Where(t => t.Priority == query.Priority);
        }

        if (!query.IncludeCompleted)
        {
            filtered = filtered.Where(t => t.Status != TaskExecutionStatus.Completed);
        }

        var totalCount = filtered.Count();
        var tasks = filtered
            .OrderBy(t => (int)t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToList();

        return Task.FromResult(RuntimeTaskListResult.Ok(tasks, totalCount));
    }

    public Task<RuntimeTaskResult> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(RuntimeTaskResult.Fail(L.T(StringKey.RuntimeTaskNotExist, taskId)));
        }

        return Task.FromResult(RuntimeTaskResult.Ok(task));
    }

    public async Task<RuntimeTaskResult> SetDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.ContainsKey(taskId))
        {
            return RuntimeTaskResult.Fail(L.T(StringKey.RuntimeTaskNotExist, taskId));
        }

        if (!_tasks.ContainsKey(dependsOnTaskId))
        {
            return RuntimeTaskResult.Fail(L.T(StringKey.DepTaskNotExist, dependsOnTaskId));
        }

        if (await _dag.WouldCreateCycleAsync(dependsOnTaskId, taskId, cancellationToken).ConfigureAwait(false))
        {
            return RuntimeTaskResult.Fail(L.T(StringKey.CircularDependencyRejected));
        }

        if (!_dag.Nodes.ContainsKey(taskId))
            await _dag.AddNodeAsync(new DagNode<string> { Id = taskId, Payload = taskId }, cancellationToken).ConfigureAwait(false);
        if (!_dag.Nodes.ContainsKey(dependsOnTaskId))
            await _dag.AddNodeAsync(new DagNode<string> { Id = dependsOnTaskId, Payload = dependsOnTaskId }, cancellationToken).ConfigureAwait(false);

        var edgeResult = await _dag.AddEdgeAsync(new DagEdge { FromId = dependsOnTaskId, ToId = taskId, Label = "DEPENDS_ON" }, cancellationToken).ConfigureAwait(false);
        if (!edgeResult.Success)
        {
            return RuntimeTaskResult.Fail(L.T(StringKey.DependencyAlreadyExists));
        }

        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Dependencies.Add(dependsOnTaskId);
        }

        _logger?.LogDebug(L.T(StringKey.RuntimeSetDepLog), taskId, dependsOnTaskId);

        return RuntimeTaskResult.Ok(task!);
    }

    public async Task<RuntimeTaskResult> RemoveDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default)
    {
        var edgeToRemove = _dag.Edges.Values
            .FirstOrDefault(e => e.FromId == dependsOnTaskId && e.ToId == taskId);
        if (edgeToRemove is null)
        {
            return RuntimeTaskResult.Fail(L.T(StringKey.DepNotExist, dependsOnTaskId));
        }

        var result = await _dag.RemoveEdgeAsync(edgeToRemove.Id, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return RuntimeTaskResult.Fail(result.ErrorMessage ?? "Failed to remove edge");
        }

        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Dependencies.Remove(dependsOnTaskId);
        }

        return RuntimeTaskResult.Ok(task!);
    }

    public Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(false);
        }

        if (task.Status != TaskExecutionStatus.Pending && task.Status != TaskExecutionStatus.Ready)
        {
            return Task.FromResult(false);
        }

        if (task.Dependencies is not { Count: > 0 })
        {
            return Task.FromResult(true);
        }

        foreach (var depId in task.Dependencies)
        {
            if (_tasks.TryGetValue(depId, out var depTask))
            {
                if (depTask.Status != TaskExecutionStatus.Completed)
                {
                    return Task.FromResult(false);
                }
            }
            else
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<RuntimeTask>> DequeueReadyTasksAsync(CancellationToken cancellationToken = default)
    {
        var completedIds = new HashSet<string>(
            _tasks.Values
                .Where(t => t.Status == TaskExecutionStatus.Completed)
                .Select(t => t.Id));

        var ready = new List<RuntimeTask>();

        foreach (var task in _tasks.Values.Where(t => t.Status == TaskExecutionStatus.Pending || t.Status == TaskExecutionStatus.Ready))
        {
            if (task.IsLightweight)
            {
                ready.Add(task);
                continue;
            }

            var allDepsMet = task.Dependencies.All(depId => completedIds.Contains(depId));
            if (allDepsMet)
            {
                ready.Add(task);
            }
        }

        var ordered = ready
            .OrderBy(t => (int)t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<RuntimeTask>>(ordered);
    }

    public async Task PersistAsync(CancellationToken cancellationToken = default)
    {
        if (_deps.FileOperationService is null || _deps.PersistenceDirectory is null)
        {
            return;
        }

        await _persistLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_deps.FileOperationService.DirectoryExists(_deps.PersistenceDirectory))
            {
                _deps.FileOperationService.CreateDirectory(_deps.PersistenceDirectory);
            }

            var durableTasks = _tasks.Values.Where(t => t.IsDurable).ToList();
            var filePath = Path.Combine(_deps.PersistenceDirectory, "runtime-tasks.json");
            var json = JsonSerializer.Serialize(durableTasks, SchedulingTasksJsonContext.Default.ListRuntimeTask);
            await _deps.FileOperationService.WriteFileAsync(filePath, json, cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(L.T(StringKey.PersistTasksLog), durableTasks.Count);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    public async Task<IReadOnlyList<RuntimeTask>> RecoverTasksAsync(string? goalId = null, CancellationToken cancellationToken = default)
    {
        if (_deps.FileOperationService is null || _deps.PersistenceDirectory is null)
        {
            return Array.Empty<RuntimeTask>();
        }

        await _persistLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var filePath = Path.Combine(_deps.PersistenceDirectory, "runtime-tasks.json");
            if (!_deps.FileOperationService.FileExists(filePath))
            {
                return Array.Empty<RuntimeTask>();
            }

            var readResult = await _deps.FileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!readResult.Success)
            {
                return Array.Empty<RuntimeTask>();
            }

            var tasks = JsonSerializer.Deserialize(readResult.Content, SchedulingTasksJsonContext.Default.ListRuntimeTask);
            if (tasks is null || tasks.Count == 0)
            {
                return Array.Empty<RuntimeTask>();
            }

            var recovered = new List<RuntimeTask>();
            foreach (var task in tasks)
            {
                if (goalId is not null && task.GoalId != goalId)
                {
                    continue;
                }

                if (task.Status == TaskExecutionStatus.Running)
                {
                    task.Status = TaskExecutionStatus.Pending;
                    task.ErrorMessage = L.T(StringKey.CrashRecoveryMsg);
                }

                _tasks[task.Id] = task;

                if (task.Dependencies is { Count: > 0 })
                {
                    _dag.AddNode(new DagNode<string> { Id = task.Id, Payload = task.Id });
                    foreach (var depId in task.Dependencies)
                    {
                        if (!_dag.Nodes.ContainsKey(depId))
                            _dag.AddNode(new DagNode<string> { Id = depId, Payload = depId });
                        _dag.TryAddEdge(new DagEdge { FromId = depId, ToId = task.Id, Label = "DEPENDS_ON" });
                    }
                }
                else
                {
                    _dag.AddNode(new DagNode<string> { Id = task.Id, Payload = task.Id });
                }

                recovered.Add(task);
            }

            _logger?.LogInformation(L.T(StringKey.RecoverTasksLog), recovered.Count);
            return recovered;
        }
        finally
        {
            _persistLock.Release();
        }
    }

    public void Clear()
    {
        _tasks.Clear();
        _dag.Clear();
        Volatile.Write(ref _taskCounter, 0);
    }

    public Task<AgentTaskResult> ExecuteRemoteAgentTaskAsync(RemoteAgentTaskDefinition definition, CancellationToken ct = default)
    {
        if (_deps.RemoteAgentTaskExecutor == null)
            throw new InvalidOperationException(L.T(StringKey.RemoteAgentTaskExecutorNotRegistered));
        return _deps.RemoteAgentTaskExecutor.ExecuteRemoteAsync(definition, ct);
    }

    public Task<WorkflowResult> ExecuteWorkflowTaskAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        if (_deps.WorkflowTaskExecutor == null)
            throw new InvalidOperationException(L.T(StringKey.WorkflowTaskExecutorNotRegistered));
        return _deps.WorkflowTaskExecutor.ExecuteWorkflowAsync(definition, ct);
    }

    public Task<string> StartMcpMonitoringAsync(McpMonitorConfig config, CancellationToken ct = default)
    {
        if (_deps.MonitorMcpTaskExecutor == null)
            throw new InvalidOperationException("MonitorMcpTaskExecutor 未注册");
        return _deps.MonitorMcpTaskExecutor.StartMonitoringAsync(config, ct);
    }

    public Task<AgentTaskResult> ExecuteLocalShellTaskAsync(LocalShellTaskDefinition definition, CancellationToken ct = default)
    {
        if (_deps.LocalShellTaskExecutor == null)
            throw new InvalidOperationException("LocalShellTaskExecutor 未注册");
        return definition.UsePowerShell
            ? _deps.LocalShellTaskExecutor.ExecutePowerShellAsync(definition, ct)
            : _deps.LocalShellTaskExecutor.ExecuteShellAsync(definition, ct);
    }

    public Task<AgentTaskResult> ExecuteInProcessTeammateAsync(InProcessTeammateDefinition definition, CancellationToken ct = default)
    {
        if (_deps.InProcessTeammateTaskExecutor == null)
            throw new InvalidOperationException("InProcessTeammateTaskExecutor 未注册");
        return _deps.InProcessTeammateTaskExecutor.ExecuteTeammateAsync(definition, ct);
    }

    private string GenerateTaskId()
    {
        var counter = Interlocked.Increment(ref _taskCounter);
        return $"rtask_{counter:D4}";
    }

    public void Dispose()
    {
        _dag.Dispose();
        _persistLock.Dispose();
    }
}
