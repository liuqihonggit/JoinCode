
namespace Core.Tests.Fakes;

/// <summary>
/// 测试用任务运行时 — 记录创建/更新调用并返回成功结果。
/// </summary>
public sealed class FakeTaskRuntime : ITaskRuntime {
    private readonly List<RuntimeTaskInput> _createdInputs = new();
    private readonly List<(string TaskId, RuntimeTaskUpdate Update)> _updates = new();

    /// <summary>获取已创建任务的输入记录列表。</summary>
    public IReadOnlyList<RuntimeTaskInput> CreatedInputs => _createdInputs;
    /// <summary>获取已更新任务的记录列表。</summary>
    public IReadOnlyList<(string TaskId, RuntimeTaskUpdate Update)> Updates => _updates;

    /// <summary>异步创建任务。</summary>
    /// <param name="input">任务创建输入。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<OperationResult<RuntimeTask?>> CreateTaskAsync(RuntimeTaskInput input, CancellationToken cancellationToken = default) {
        _createdInputs.Add(input);
        var task = new RuntimeTask {
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

    /// <summary>异步更新任务。</summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="update">任务更新内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<OperationResult<RuntimeTask?>> UpdateTaskAsync(string taskId, RuntimeTaskUpdate update, CancellationToken cancellationToken = default) {
        _updates.Add((taskId, update));
        return Task.FromResult(OperationResult<RuntimeTask?>.Ok(null));
    }

    /// <summary>异步列出任务。</summary>
    /// <param name="query">任务查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<RuntimeTaskListResult> ListTasksAsync(RuntimeTaskQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(RuntimeTaskListResult.Ok(Array.Empty<RuntimeTask>(), 0));

    /// <summary>异步获取指定任务。</summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<OperationResult<RuntimeTask?>> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RuntimeTask?>.Ok(null));

    /// <summary>异步设置任务依赖。</summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="dependsOnTaskId">依赖的任务 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<OperationResult<RuntimeTask?>> SetDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RuntimeTask?>.Ok(null));

    /// <summary>异步移除任务依赖。</summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="dependsOnTaskId">依赖的任务 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<OperationResult<RuntimeTask?>> RemoveDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RuntimeTask?>.Ok(null));

    /// <summary>异步判断任务是否可执行。</summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <summary>异步出队就绪任务列表。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<IReadOnlyList<RuntimeTask>> DequeueReadyTasksAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RuntimeTask>>(Array.Empty<RuntimeTask>());

    /// <summary>异步持久化任务状态。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task PersistAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>异步恢复任务列表。</summary>
    /// <param name="goalId">目标 ID（可选，为 null 则恢复全部）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<IReadOnlyList<RuntimeTask>> RecoverTasksAsync(string? goalId = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RuntimeTask>>(Array.Empty<RuntimeTask>());

    /// <summary>清除所有记录。</summary>
    public void Clear() {
        _createdInputs.Clear();
        _updates.Clear();
    }
}