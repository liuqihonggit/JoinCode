namespace JoinCode.Abstractions.Interfaces;

public interface ITaskRuntime {
    /// <summary>异步创建运行时任务。</summary>
    Task<OperationResult<RuntimeTask?>> CreateTaskAsync(RuntimeTaskInput input, CancellationToken cancellationToken = default);

    /// <summary>异步更新指定任务。</summary>
    Task<OperationResult<RuntimeTask?>> UpdateTaskAsync(string taskId, RuntimeTaskUpdate update, CancellationToken cancellationToken = default);

    /// <summary>异步按查询条件列出任务。</summary>
    Task<RuntimeTaskListResult> ListTasksAsync(RuntimeTaskQuery query, CancellationToken cancellationToken = default);

    /// <summary>异步获取指定任务。</summary>
    Task<OperationResult<RuntimeTask?>> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>异步设置任务依赖关系。</summary>
    Task<OperationResult<RuntimeTask?>> SetDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default);

    /// <summary>异步移除任务依赖关系。</summary>
    Task<OperationResult<RuntimeTask?>> RemoveDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default);

    /// <summary>异步判断指定任务是否可执行。</summary>
    Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>异步出队所有就绪任务。</summary>
    Task<IReadOnlyList<RuntimeTask>> DequeueReadyTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>异步持久化当前任务状态。</summary>
    Task PersistAsync(CancellationToken cancellationToken = default);

    /// <summary>异步恢复任务,可按目标标识过滤。</summary>
    Task<IReadOnlyList<RuntimeTask>> RecoverTasksAsync(string? goalId = null, CancellationToken cancellationToken = default);

    /// <summary>清空所有任务。</summary>
    void Clear();
}
