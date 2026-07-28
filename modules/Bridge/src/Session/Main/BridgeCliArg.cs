namespace Core.Bridge;

/// <summary>
/// Bridge 远程控制命令行参数枚举 — [CliOption] 由 CliOptionGenerator 自动生成 BridgeCliArgParser + BridgeCliArgParseResult
/// </summary>
public enum BridgeCliArg
{
    [CliOption("--verbose", "-v", "详细日志")]
    Verbose,

    [CliOption("--sandbox", "", "启用沙箱")]
    Sandbox,

    [CliOption("--no-sandbox", "", "禁用沙箱", IsNegation = true)]
    NoSandbox,

    [CliOption("--debug-file", "", "调试日志文件", AcceptsValue = true)]
    DebugFile,

    [CliOption("--session-timeout", "", "会话超时（秒）", AcceptsValue = true)]
    SessionTimeout,

    [CliOption("--permission-mode", "", "权限模式", AcceptsValue = true)]
    PermissionMode,

    [CliOption("--name", "", "会话名称", AcceptsValue = true)]
    Name,

    [CliOption("--spawn", "", "子进程生成模式", AcceptsValue = true)]
    Spawn,

    [CliOption("--capacity", "", "最大并发会话数", AcceptsValue = true)]
    Capacity,

    [CliOption("--create-session-in-dir", "", "在目录中创建会话")]
    CreateSessionInDir,

    [CliOption("--no-create-session-in-dir", "", "不在目录中创建会话", IsNegation = true)]
    NoCreateSessionInDir,

    [CliOption("--session-id", "", "恢复指定会话", AcceptsValue = true)]
    SessionId,

    [CliOption("--continue", "-c", "继续上次会话")]
    Continue,

    [CliOption("--help", "-h", "显示帮助")]
    Help,
}
