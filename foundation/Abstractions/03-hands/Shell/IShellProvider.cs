namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Shell 提供者接口 — 对齐 TS ShellProvider
/// 封装不同 Shell 类型（Bash/PowerShell/Python/Cmd）的命令构建、进程启动和环境变量注入
/// </summary>
public interface IShellProvider
{
    /// <summary>
    /// Shell 类型 — 对齐 TS ShellType
    /// </summary>
    ShellType Type { get; }

    /// <summary>
    /// Shell 可执行文件路径 — 对齐 TS ShellProvider.shellPath
    /// </summary>
    string ShellPath { get; }

    /// <summary>
    /// 人类可读的描述名称 — 用于提示词注入和日志
    /// 示例: "Git Bash 5.2", "PowerShell Core 7.4", "Python 3.12", "CMD"
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 是否使用分离进程 — 对齐 TS ShellProvider.detached
    /// </summary>
    bool Detached { get; }

    /// <summary>
    /// Shell 版本信息 — 对齐 TS powershellDetection / bash --version
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 标准输出编码 — 对齐 TS Shell.ts stdoutEncoding
    /// </summary>
    Encoding OutputEncoding { get; }

    /// <summary>
    /// 标准错误编码 — 默认与 OutputEncoding 相同
    /// </summary>
    Encoding ErrorEncoding { get; }

    /// <summary>
    /// 构建执行命令 — 对齐 TS ShellProvider.buildExecCommand()
    /// </summary>
    Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command,
        ShellExecOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取进程启动参数 — 对齐 TS ShellProvider.getSpawnArgs()
    /// </summary>
    string[] GetSpawnArgs(string commandString);

    /// <summary>
    /// 获取环境变量覆盖 — 对齐 TS ShellProvider.getEnvironmentOverrides()
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shell 信息快照 — 用于提示词注入，不暴露 IShellProvider 实例
/// </summary>
public sealed record ShellInfo
{
    public required ShellType Type { get; init; }
    public required string DisplayName { get; init; }
    public required string ShellPath { get; init; }
    public required string Version { get; init; }
}



/// <summary>
/// Shell 执行命令构建选项 — 对齐 TS buildExecCommand opts 参数
/// </summary>
public sealed class ShellExecOptions
{
    /// <summary>
    /// 会话 ID — 用于 CWD 追踪文件命名
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 沙箱临时目录 — 对齐 TS sandboxTmpDir
    /// </summary>
    public string? SandboxTmpDir { get; init; }

    /// <summary>
    /// 是否使用沙箱 — 对齐 TS useSandbox
    /// </summary>
    public bool UseSandbox { get; init; }
}

/// <summary>
/// Shell 执行命令构建结果 — 对齐 TS buildExecCommand 返回值
/// </summary>
public sealed record ShellExecCommandResult
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
