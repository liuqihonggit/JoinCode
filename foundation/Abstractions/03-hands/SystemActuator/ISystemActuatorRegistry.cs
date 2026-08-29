namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 系统执行器注册表接口 — 按 Kind 查找执行器 + 统一管理跨执行器的后台任务
/// 合并原 CapabilityCache + ProviderFactory + BackgroundTaskService 三层
/// </summary>
public interface ISystemActuatorRegistry : IRegistry
{
    /// <summary>
    /// 按 Kind 获取执行器
    /// </summary>
    ISystemActuator Get(SystemActuatorKind kind);

    /// <summary>
    /// 尝试按 Kind 获取执行器
    /// </summary>
    bool TryGet(SystemActuatorKind kind, [NotNullWhen(true)] out ISystemActuator? actuator);

    /// <summary>
    /// 所有已注册的执行器类型
    /// </summary>
    IReadOnlyCollection<SystemActuatorKind> RegisteredKinds { get; }

    /// <summary>
    /// 获取所有执行器信息快照 — 用于提示词注入
    /// </summary>
    IReadOnlyDictionary<SystemActuatorKind, SystemActuatorInfo> GetAllInfos();

    /// <summary>
    /// 注册已后台化的命令上下文
    /// </summary>
    Task<SystemActuatorBackgroundTaskInfo> RegisterContextAsync(
        ISystemActuatorCommandContext context,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取后台任务
    /// </summary>
    Task<SystemActuatorBackgroundTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出所有后台任务
    /// </summary>
    Task<List<SystemActuatorBackgroundTaskInfo>> ListTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消后台任务
    /// </summary>
    Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 等待后台任务完成
    /// </summary>
    Task<SystemActuatorBackgroundTaskInfo> WaitForTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取后台任务输出
    /// </summary>
    Task<string> GetTaskOutputAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出指定 Agent 的后台任务
    /// </summary>
    Task<List<SystemActuatorBackgroundTaskInfo>> ListTasksForAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消指定 Agent 的所有后台任务
    /// </summary>
    Task<int> CancelTasksForAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制杀死所有运行中的后台任务 — tree-kill 杀死进程树
    /// </summary>
    Task<int> KillAllRunningAsync(CancellationToken cancellationToken = default);
}
