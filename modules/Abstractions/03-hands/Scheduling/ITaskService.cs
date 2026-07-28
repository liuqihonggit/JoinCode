
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 任务服务接口
/// </summary>
public interface ITaskService : IDisposable
{
    /// <summary>
    /// 创建任务
    /// </summary>
    Task<OperationResult<TaskItem?>> CreateTaskAsync(
        string title,
        string? description,
        string? assignee,
        DateTime? dueDate,
        string priority,
        List<string>? tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出任务
    /// </summary>
    Task<TaskListResult> ListTasksAsync(
        string? status,
        string? assignee,
        string? priority,
        int limit,
        int offset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新任务
    /// </summary>
    Task<OperationResult<TaskItem?>> UpdateTaskAsync(
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止任务
    /// </summary>
    Task<OperationResult<TaskItem?>> StopTaskAsync(
        string taskId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取任务
    /// </summary>
    Task<TaskItem?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取任务依赖关系
    /// </summary>
    Task<IReadOnlyList<TaskDependency>> GetTaskDependenciesAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置任务依赖关系
    /// </summary>
    Task<OperationResult<TaskItem?>> SetTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        TaskDependencyType dependencyType = TaskDependencyType.Blocks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除任务依赖关系
    /// </summary>
    Task<OperationResult<TaskItem?>> RemoveTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查任务是否可以执行（所有依赖是否满足）
    /// </summary>
    Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止正在运行的任务
    /// </summary>
    Task<bool> StopTaskAsync(string taskId, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有正在运行的任务
    /// </summary>
    Task<IReadOnlyList<RunningTaskInfo>> GetRunningTasksAsync(CancellationToken cancellationToken = default);
}
