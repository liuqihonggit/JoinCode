namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Shell 提供者接口 — 对齐 TS ShellProvider
/// 封装不同 Shell 类型（Bash/PowerShell）的命令构建、进程启动和环境变量注入
/// </summary>
public interface IShellProvider
{
    /// <summary>
    /// Shell 类型 — 对齐 TS ShellType
    /// </summary>
    ShellProviderType Type { get; }

    /// <summary>
    /// Shell 可执行文件路径 — 对齐 TS ShellProvider.shellPath
    /// Bash: Git Bash bash.exe 路径; PowerShell: powershell.exe 或 pwsh.exe 路径
    /// </summary>
    string ShellPath { get; }

    /// <summary>
    /// 是否使用分离进程 — 对齐 TS ShellProvider.detached
    /// Bash 为 true（独立进程组）; PowerShell 为 false
    /// </summary>
    bool Detached { get; }

    /// <summary>
    /// Shell 版本信息 — 对齐 TS powershellDetection / bash --version
    /// Bash: "5.2.21"; PowerShell: "7.4.6" 或 "5.1.22621.1"
    /// 用于提示词注入，让 LLM 知道当前 Shell 版本以生成正确的命令语法
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 标准输出编码 — 对齐 TS Shell.ts stdoutEncoding
    /// 默认 UTF-8，子类可覆盖以指定特定编码
    /// </summary>
    Encoding OutputEncoding { get; }

    /// <summary>
    /// 标准错误编码 — 默认与 OutputEncoding 相同
    /// </summary>
    Encoding ErrorEncoding { get; }

    /// <summary>
    /// 构建执行命令 — 对齐 TS ShellProvider.buildExecCommand()
    /// 返回完整的命令字符串（含 shell 初始化、CWD 追踪等）和 CWD 追踪文件路径
    /// </summary>
    Task<ShellExecCommandResult> BuildExecCommandAsync(
        string command,
        ShellExecOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取进程启动参数 — 对齐 TS ShellProvider.getSpawnArgs()
    /// Bash: ["-c", commandString]; PowerShell: ["-NoProfile", "-NonInteractive", "-Command", commandString]
    /// </summary>
    string[] GetSpawnArgs(string commandString);

    /// <summary>
    /// 获取环境变量覆盖 — 对齐 TS ShellProvider.getEnvironmentOverrides()
    /// 注入 CLAUDECODE=1, GIT_EDITOR=true, TMPDIR 等环境变量
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shell 提供者类型 — 对齐 TS ShellType
/// </summary>
public enum ShellProviderType
{
    /// <summary>
    /// Bash (Git Bash on Windows) — 对齐 TS 'bash'
    /// </summary>
    Bash,

    /// <summary>
    /// PowerShell — 对齐 TS 'powershell'
    /// </summary>
    PowerShell
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
