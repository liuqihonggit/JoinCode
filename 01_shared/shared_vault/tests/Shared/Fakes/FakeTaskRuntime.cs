
namespace Core.Tests.Fakes;

/// <summary>
/// 测试用任务运行时 — 记录创建/更新调用并返回成功结果。
/// </summary>
public sealed class FakeTaskRuntime : ITaskRuntime
{
    private readonly List<RuntimeTaskInput> _createdInputs = new();
    private readonly List<(string TaskId, RuntimeTaskUpdate Update)> _updates = new();

    public IReadOnlyList<RuntimeTaskInput> CreatedInputs => _createdInputs;
    public IReadOnlyList<(string TaskId, RuntimeTaskUpdate Update)> Updates => _updates;

    public Task<OperationResult<RuntimeTask?>> CreateTaskAsync(RuntimeTaskInput input, CancellationToken cancellationToken = default)
    {
        _createdInputs.Add(input);
        var task = new RuntimeTask
        {
            Id = Guid.NewGuid().ToString("N"),
            Description = input.Description,
            Status = TaskExecutionStatus.Pending,
            Priority = input.Priority,
            GoalId = input.GoalId,
            IsLightweight = input.IsLightweight,
            IsDurable = input.IsDurable
        };
        return Task.FromResult(OperationResult<RuntimeTask?>.Ok(task));
    }

    public Task<OperationResult<RuntimeTask?>> UpdateTaskAsync(string taskId, RuntimeTaskUpdate update, CancellationToken cancellationToken = default)
    {
        _updates.Add((taskId, update));
        return Task.FromResult(OperationResult<RuntimeTask?>.Ok(null));
    }

    public Task<RuntimeTaskListResult> ListTasksAsync(RuntimeTaskQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(RuntimeTaskListResult.Ok(Array.Empty<RuntimeTask>(), 0));

    public Task<OperationResult<RuntimeTask?>> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RuntimeTask?>.Ok(null));

    public Task<OperationResult<RuntimeTask?>> SetDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RuntimeTask?>.Ok(null));

    public Task<OperationResult<RuntimeTask?>> RemoveDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RuntimeTask?>.Ok(null));

    public Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<RuntimeTask>> DequeueReadyTasksAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RuntimeTask>>(Array.Empty<RuntimeTask>());

    public Task PersistAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<RuntimeTask>> RecoverTasksAsync(string? goalId = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RuntimeTask>>(Array.Empty<RuntimeTask>());

    public void Clear()
    {
        _createdInputs.Clear();
        _updates.Clear();
    }
}
