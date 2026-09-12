namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 系统执行器接口 — 统一 Bash/PowerShell/Cmd/Python 等执行器的命令构建、执行、后台启动
/// 消费者通过 ISystemActuatorRegistry.Get(kind) 获取实例，多态调用
/// </summary>
public interface ISystemActuator
{
    /// <summary>
    /// 执行器类型标识
    /// </summary>
    SystemActuatorKind Kind { get; }

    /// <summary>
    /// 可执行文件路径
    /// </summary>
    string ShellPath { get; }

    /// <summary>
    /// 人类可读的描述名称 — 用于提示词注入和日志
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 是否使用分离进程
    /// </summary>
    bool Detached { get; }

    /// <summary>
    /// 版本信息
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 标准输出编码
    /// </summary>
    Encoding OutputEncoding { get; }

    /// <summary>
    /// 标准错误编码
    /// </summary>
    Encoding ErrorEncoding { get; }

    /// <summary>
    /// 构建执行命令
    /// </summary>
    Task<SystemActuatorExecCommandResult> BuildExecCommandAsync(
        string command,
        SystemActuatorExecOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取进程启动参数
    /// </summary>
    string[] GetSpawnArgs(string commandString);

    /// <summary>
    /// 获取环境变量覆盖
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行命令并返回结果
    /// </summary>
    Task<SystemActuatorExecutionResult> ExecuteAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动可后台化的命令 — 返回命令上下文，支持超时自动后台化、手动后台化
    /// </summary>
    Task<ISystemActuatorCommandContext> StartWithBackgroundSupportAsync(
        string command,
        int? timeout = null,
        string? workingDirectory = null,
        bool shouldAutoBackground = true,
        bool disableSandbox = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 系统执行器生命周期管理接口 — 上下文压缩时的链式清理
/// </summary>
public interface ISystemActuatorLifecycle : IAsyncDisposable
{
    /// <summary>
    /// 当前生命周期状态
    /// </summary>
    SystemActuatorLifecycleState LifecycleState { get; }

    /// <summary>
    /// 上下文压缩时调用 — Running→后台化; Backgrounded→保留但截断输出; Completed→释放
    /// </summary>
    Task CompactAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制终止 — 杀进程树、回收内存
    /// </summary>
    Task TerminateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 系统执行器命令上下文接口 — 封装正在运行的进程，支持前台转后台操作
/// </summary>
public interface ISystemActuatorCommandContext : ISystemActuatorLifecycle
{
    /// <summary>
    /// 任务 ID
    /// </summary>
    string TaskId { get; }

    /// <summary>
    /// 当前状态
    /// </summary>
    SystemActuatorCommandStatus Status { get; }

    /// <summary>
    /// 执行结果 Task
    /// </summary>
    Task<SystemActuatorExecutionResult> ResultTask { get; }

    /// <summary>
    /// 原始命令
    /// </summary>
    string Command { get; }

    /// <summary>
    /// 是否允许自动后台化
    /// </summary>
    bool ShouldAutoBackground { get; }

    /// <summary>
    /// 将进程转为后台运行 — 不杀进程，只改变状态标记
    /// </summary>
    bool Background(string taskId);

    /// <summary>
    /// 杀进程 — tree-kill(SIGKILL) 强制终止整个进程树
    /// </summary>
    void Kill();

    /// <summary>
    /// 中断进程 — 用户提交新消息时触发，不杀进程，转为后台任务
    /// </summary>
    bool Interrupt();

    /// <summary>
    /// 启动 Assistant 自动后台化定时器 — 命令运行超过 15s 自动转后台
    /// </summary>
    void StartAssistantAutoBackgroundTimer();

    /// <summary>
    /// 获取当前已收集的 stdout
    /// </summary>
    string GetCurrentStdout();

    /// <summary>
    /// 获取当前已收集的 stderr
    /// </summary>
    string GetCurrentStderr();

    /// <summary>
    /// 输出文件路径 — 后台模式下输出溢出到磁盘时的文件路径
    /// </summary>
    string? OutputFilePath { get; }
}
