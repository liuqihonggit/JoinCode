namespace Core.Bridge;

/// <summary>
/// Bridge 远程控制命令行参数枚举 — [CliOption] 由 CliOptionGenerator 自动生成 BridgeCliArgParser + BridgeCliArgParseResult
/// 参数名引用 JccCliArgConstants（由 JccCliArg 枚举 + [EnumValue] 生成），确保参数名单一数据源
/// </summary>
public enum BridgeCliArg
{
    [CliOption(JccCliArgConstants.DebugLog, "-d", "调试日志")]
    DebugLog,

    [CliOption(JccCliArgConstants.Sandbox, "", "启用沙箱")]
    Sandbox,

    [CliOption(JccCliArgConstants.NoSandbox, "", "禁用沙箱", IsNegation = true)]
    NoSandbox,

    [CliOption(JccCliArgConstants.DebugFile, "", "调试日志文件", AcceptsValue = true)]
    DebugFile,

    [CliOption(JccCliArgConstants.SessionTimeout, "", "会话超时（秒）", AcceptsValue = true)]
    SessionTimeout,

    [CliOption(JccCliArgConstants.PermissionMode, "", "权限模式", AcceptsValue = true)]
    PermissionMode,

    [CliOption(JccCliArgConstants.Name, "", "会话名称", AcceptsValue = true)]
    Name,

    [CliOption(JccCliArgConstants.Spawn, "", "子进程生成模式", AcceptsValue = true)]
    Spawn,

    [CliOption(JccCliArgConstants.Capacity, "", "最大并发会话数", AcceptsValue = true)]
    Capacity,

    [CliOption(JccCliArgConstants.CreateSessionInDir, "", "在目录中创建会话")]
    CreateSessionInDir,

    [CliOption(JccCliArgConstants.NoCreateSessionInDir, "", "不在目录中创建会话", IsNegation = true)]
    NoCreateSessionInDir,

    [CliOption(JccCliArgConstants.SessionId, "", "恢复指定会话", AcceptsValue = true)]
    SessionId,

    [CliOption(JccCliArgConstants.Continue, "-c", "继续上次会话")]
    Continue,

    [CliOption(JccCliArgConstants.Help, "-h", "显示帮助")]
    Help,
}
