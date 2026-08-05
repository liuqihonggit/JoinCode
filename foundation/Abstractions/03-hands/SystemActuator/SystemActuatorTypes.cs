namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 系统执行器信息快照 — 用于提示词注入，不暴露 ISystemActuator 实例
/// </summary>
public sealed record SystemActuatorInfo
{
    public required SystemActuatorKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required string ShellPath { get; init; }
    public required string Version { get; init; }
}

/// <summary>
/// 系统执行器能力描述 — 长命缓存，只检测一次
/// 封装执行器的静态属性：类型、路径、版本、编码、DisplayName
/// </summary>
public sealed class SystemActuatorCapability
{
    public required SystemActuatorKind Kind { get; init; }
    public string ShellPath { get; init; } = "";
    public string Version { get; init; } = "unknown";
    public string DisplayName { get; init; } = "";
    public bool Detached { get; init; }
    public Encoding OutputEncoding { get; init; } = Encoding.UTF8;
    public Encoding ErrorEncoding { get; init; } = Encoding.UTF8;

    /// <summary>
    /// 是否为 PowerShell Core (7+) — 仅 PowerShell 类型有值
    /// </summary>
    public bool IsPowerShellCore { get; init; }

    /// <summary>
    /// 转为 SystemActuatorInfo 快照 — 用于提示词注入
    /// </summary>
    public SystemActuatorInfo ToInfo() => new()
    {
        Kind = Kind,
        DisplayName = DisplayName,
        ShellPath = ShellPath,
        Version = Version,
    };
}

/// <summary>
/// 系统执行器命令构建选项
/// </summary>
public sealed class SystemActuatorExecOptions
{
    /// <summary>
    /// 会话 ID — 用于 CWD 追踪文件命名
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 沙箱临时目录
    /// </summary>
    public string? SandboxTmpDir { get; init; }

    /// <summary>
    /// 是否使用沙箱
    /// </summary>
    public bool UseSandbox { get; init; }
}

/// <summary>
/// 系统执行器命令构建结果
/// </summary>
public sealed record SystemActuatorExecCommandResult
{
    /// <summary>
    /// 完整命令字符串（含 shell 初始化、CWD 追踪等）
    /// </summary>
    public required string CommandString { get; init; }

    /// <summary>
    /// CWD 追踪文件路径 — 命令执行后写入当前工作目录
    /// </summary>
    public required string CwdFilePath { get; init; }
}

/// <summary>
/// 系统执行器命令状态
/// </summary>
public enum SystemActuatorCommandStatus
{
    Running,
    Backgrounded,
    Completed,
    Killed
}

/// <summary>
/// 系统执行器生命周期状态
/// </summary>
public enum SystemActuatorLifecycleState
{
    /// <summary>活跃运行中</summary>
    Active,
    /// <summary>已后台化，仍占用资源</summary>
    Backgrounded,
    /// <summary>已完成，可释放</summary>
    Completed,
    /// <summary>已终止</summary>
    Terminated,
}

/// <summary>
/// 系统执行器执行结果
/// </summary>
public sealed record SystemActuatorExecutionResult
{
    /// <summary>
    /// 内联输出上限（30K）
    /// </summary>
    public const int MaxInlineOutputChars = 30_000;

    /// <summary>
    /// 持久化输出硬上限（64MB）
    /// </summary>
    public const long MaxPersistedSizeBytes = 64 * 1024 * 1024;

    /// <summary>
    /// 预览大小（2KB）
    /// </summary>
    public const int PreviewSizeBytes = 2048;

    public required string Stdout { get; init; }
    public required string Stderr { get; init; }
    public int? ExitCode { get; init; }
    public int? ProcessId { get; init; }
    public bool Interrupted { get; init; }
    public bool Success => ExitCode == 0 && !Interrupted;
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 大输出持久化路径 — 输出超过 MaxInlineOutputChars 时，完整输出保存到磁盘
    /// </summary>
    public string? PersistedOutputPath { get; init; }

    /// <summary>
    /// 持久化输出原始大小（字节）
    /// </summary>
    public long? PersistedOutputSize { get; init; }

    /// <summary>
    /// 是否被用户手动后台化
    /// </summary>
    public bool BackgroundedByUser { get; init; }

    /// <summary>
    /// 是否被 Assistant 模式自动后台化
    /// </summary>
    public bool AssistantAutoBackgrounded { get; init; }

    /// <summary>
    /// 后台任务 ID
    /// </summary>
    public string? BackgroundTaskId { get; init; }

    /// <summary>
    /// CWD 是否被重置
    /// </summary>
    public bool CwdWasReset { get; init; }

    /// <summary>
    /// 生成大输出持久化消息
    /// </summary>
    public string BuildPersistedOutputMessage()
    {
        if (PersistedOutputPath is null) return Stdout;

        var preview = Stdout.Length > PreviewSizeBytes
            ? Stdout[..PreviewSizeBytes]
            : Stdout;

        var hasMore = Stdout.Length > PreviewSizeBytes;
        var result = new PersistedToolResult
        {
            Filepath = PersistedOutputPath,
            OriginalSize = (int)(PersistedOutputSize ?? Stdout.Length),
            IsJson = false,
            Preview = preview,
            HasMore = hasMore
        };

        return ContentReplacementConstants.BuildPersistedOutputMessage(result);
    }

    public static SystemActuatorExecutionResult SuccessResult(string stdout, string stderr, int? exitCode = 0)
        => new()
        {
            Stdout = stdout,
            Stderr = stderr,
            ExitCode = exitCode,
            Interrupted = false
        };

    public static SystemActuatorExecutionResult FailureResult(string errorMessage, string stdout = "", string stderr = "")
        => new()
        {
            Stdout = stdout,
            Stderr = stderr,
            ExitCode = -1,
            Interrupted = false,
            ErrorMessage = errorMessage
        };

    public static SystemActuatorExecutionResult TimeoutResult(int timeoutMs)
        => new()
        {
            Stdout = string.Empty,
            Stderr = $"Command timed out ({timeoutMs}ms)",
            ExitCode = -1,
            Interrupted = true,
            ErrorMessage = "Timeout"
        };
}

/// <summary>
/// 系统执行器后台任务信息
/// </summary>
public sealed record SystemActuatorBackgroundTaskInfo
{
    public required string TaskId { get; init; }
    public required string Command { get; init; }
    public required TaskExecutionStatus Status { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public int? ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? AgentId { get; init; }
}

/// <summary>
/// 系统执行器后台化常量
/// </summary>
public static class SystemActuatorBackgroundConstants
{
    /// <summary>
    /// 后台化预算（15s）— Assistant 模式下命令运行超过此时间自动转后台
    /// </summary>
    public const int AssistantBlockingBudgetMs = 15_000;

    /// <summary>
    /// 前台注册阈值（2s）— 命令运行超过此时间才注册为前台任务
    /// </summary>
    public const int ProgressThresholdMs = 2_000;

    /// <summary>
    /// 判断命令是否允许自动后台化
    /// </summary>
    public static bool IsAutoBackgroundAllowed(string command)
    {
        var trimmed = command.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        var baseCmd = spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;
        baseCmd = Path.GetFileNameWithoutExtension(baseCmd);
        return !DisallowedAutoBackgroundCommands.Contains(baseCmd);
    }

    /// <summary>
    /// 禁止自动后台化的命令 — Bash: sleep; PowerShell: start-sleep, sleep
    /// </summary>
    internal static readonly FrozenSet<string> DisallowedAutoBackgroundCommands = new[] { "sleep", "start-sleep" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
