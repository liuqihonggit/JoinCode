
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 任务协调器接口 - 负责管理和协调多 Agent 任务的执行
/// </summary>
public interface IAgentTaskCoordinator
{
    /// <summary>
    /// 执行任务
    /// </summary>
    /// <param name="context">任务上下文</param>
    /// <param name="agent">执行任务的 Agent</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务结果</returns>
    Task<IAgentTaskResult> ExecuteTaskAsync(
        IAgentTaskContext context,
        IAgent agent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量执行任务
    /// </summary>
    /// <param name="contexts">任务上下文列表</param>
    /// <param name="agentFactory">Agent 工厂函数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务结果列表</returns>
    Task<IReadOnlyList<IAgentTaskResult>> ExecuteTasksAsync(
        IEnumerable<IAgentTaskContext> contexts,
        Func<IAgentTaskContext, IAgent> agentFactory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 并行执行任务
    /// </summary>
    /// <param name="contexts">任务上下文列表</param>
    /// <param name="agentFactory">Agent 工厂函数</param>
    /// <param name="maxParallelism">最大并行度</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务结果列表</returns>
    Task<IReadOnlyList<IAgentTaskResult>> ExecuteTasksParallelAsync(
        IEnumerable<IAgentTaskContext> contexts,
        Func<IAgentTaskContext, IAgent> agentFactory,
        int maxParallelism = 4,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取任务状态
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务状态</returns>
    Task<TaskExecutionStatus> GetTaskStatusAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取任务结果
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务结果（如果已完成）</returns>
    Task<IAgentTaskResult?> GetTaskResultAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消任务
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="reason">取消原因</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功取消</returns>
    Task<bool> CancelTaskAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消所有任务
    /// </summary>
    /// <param name="reason">取消原因</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>被取消的任务 ID 列表</returns>
    Task<IReadOnlyList<string>> CancelAllTasksAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 等待任务完成
    /// </summary>
    /// <param name="taskId">任务 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务结果</returns>
    Task<IAgentTaskResult> WaitForTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 等待所有任务完成
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有任务结果</returns>
    Task<IReadOnlyList<IAgentTaskResult>> WaitForAllTasksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取正在运行的任务列表
    /// </summary>
    /// <returns>任务上下文列表</returns>
    IReadOnlyList<IAgentTaskContext> GetRunningTasks();

    /// <summary>
    /// 获取已完成的任务列表
    /// </summary>
    /// <returns>任务结果列表</returns>
    IReadOnlyList<IAgentTaskResult> GetCompletedTasks();

    /// <summary>
    /// 任务状态变更事件
    /// </summary>
    event EventHandler<AgentTaskStatusChangedEventArgs>? TaskStatusChanged;

    /// <summary>
    /// 任务完成事件
    /// </summary>
    event EventHandler<AgentTaskCompletedEventArgs>? TaskCompleted;
}

/// <summary>
/// Agent 任务状态变更事件参数
/// </summary>
public sealed class AgentTaskStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// 旧状态
    /// </summary>
    public TaskExecutionStatus OldStatus { get; }

    /// <summary>
    /// 新状态
    /// </summary>
    public TaskExecutionStatus NewStatus { get; }

    /// <summary>
    /// 状态变更时间
    /// </summary>
    public DateTime ChangedAt { get; }

    /// <summary>
    /// 附加消息
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public AgentTaskStatusChangedEventArgs(
        string taskId,
        TaskExecutionStatus oldStatus,
        TaskExecutionStatus newStatus,
        string? message = null)
    {
        TaskId = taskId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        ChangedAt = DateTime.UtcNow;
        Message = message;
    }
}

/// <summary>
/// Agent 任务完成事件参数
/// </summary>
public sealed class AgentTaskCompletedEventArgs : EventArgs
{
    /// <summary>
    /// 任务结果
    /// </summary>
    public IAgentTaskResult Result { get; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime CompletedAt { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public AgentTaskCompletedEventArgs(IAgentTaskResult result)
    {
        Result = result;
        CompletedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Agent 任务协调器配置
/// </summary>
public sealed record AgentTaskCoordinatorConfig
{
    /// <summary>
    /// 默认最大并行度
    /// </summary>
    public int DefaultMaxParallelism { get; init; } = 4;

    /// <summary>
    /// 任务超时时间（默认 5 分钟）
    /// </summary>
    public TimeSpan TaskTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 是否启用任务重试
    /// </summary>
    public bool EnableRetry { get; init; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// 重试延迟
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 是否启用任务依赖检查
    /// </summary>
    public bool EnableDependencyCheck { get; init; } = true;

    /// <summary>
    /// 任务完成清理延迟
    /// </summary>
    public TimeSpan CleanupDelay { get; init; } = TimeSpan.FromMinutes(10);
}
