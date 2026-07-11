namespace JoinCode.Abstractions.Interfaces;

public interface ITaskRuntime
{
    Task<RuntimeTaskResult> CreateTaskAsync(RuntimeTaskInput input, CancellationToken cancellationToken = default);

    Task<RuntimeTaskResult> UpdateTaskAsync(string taskId, RuntimeTaskUpdate update, CancellationToken cancellationToken = default);

    Task<RuntimeTaskListResult> ListTasksAsync(RuntimeTaskQuery query, CancellationToken cancellationToken = default);

    Task<RuntimeTaskResult> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    Task<RuntimeTaskResult> SetDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default);

    Task<RuntimeTaskResult> RemoveDependencyAsync(string taskId, string dependsOnTaskId, CancellationToken cancellationToken = default);

    Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuntimeTask>> DequeueReadyTasksAsync(CancellationToken cancellationToken = default);

    Task PersistAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuntimeTask>> RecoverTasksAsync(string? goalId = null, CancellationToken cancellationToken = default);

    void Clear();
}
