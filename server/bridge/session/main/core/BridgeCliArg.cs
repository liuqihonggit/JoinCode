namespace Core.Bridge;

/// <summary>
/// Bridge 远程控制命令行参数枚举 — [CliOption] 由 CliOptionGenerator 自动生成 BridgeCliArgParser + BridgeCliArgParseResult
/// 参数名引用 JccCliArgConstants（由 JccCliArg 枚举 + [EnumValue] 生成），确保参数名单一数据源
/// </summary>
public enum BridgeCliArg
{
    /// <summary>调试日志</summary>
    [CliOption(JccCliArgConstants.DebugLog, "-d", "调试日志")]
    DebugLog,

    /// <summary>启用沙箱</summary>
    [CliOption(JccCliArgConstants.Sandbox, "", "启用沙箱")]
    Sandbox,

    /// <summary>禁用沙箱</summary>
    [CliOption(JccCliArgConstants.NoSandbox, "", "禁用沙箱", IsNegation = true)]
    NoSandbox,

    /// <summary>调试日志文件</summary>
    [CliOption(JccCliArgConstants.DebugFile, "", "调试日志文件", AcceptsValue = true)]
    DebugFile,

    /// <summary>会话超时（秒）</summary>
    [CliOption(JccCliArgConstants.SessionTimeout, "", "会话超时（秒）", AcceptsValue = true)]
    SessionTimeout,

    /// <summary>权限模式</summary>
    [CliOption(JccCliArgConstants.PermissionMode, "", "权限模式", AcceptsValue = true)]
    PermissionMode,

    /// <summary>会话名称</summary>
    [CliOption(JccCliArgConstants.Name, "", "会话名称", AcceptsValue = true)]
    Name,

    /// <summary>子进程生成模式</summary>
    [CliOption(JccCliArgConstants.Spawn, "", "子进程生成模式", AcceptsValue = true)]
    Spawn,

    /// <summary>最大并发会话数</summary>
    [CliOption(JccCliArgConstants.Capacity, "", "最大并发会话数", AcceptsValue = true)]
    Capacity,

    /// <summary>在目录中创建会话</summary>
    [CliOption(JccCliArgConstants.CreateSessionInDir, "", "在目录中创建会话")]
    CreateSessionInDir,

    /// <summary>不在目录中创建会话</summary>
    [CliOption(JccCliArgConstants.NoCreateSessionInDir, "", "不在目录中创建会话", IsNegation = true)]
    NoCreateSessionInDir,

    /// <summary>恢复指定会话</summary>
    [CliOption(JccCliArgConstants.SessionId, "", "恢复指定会话", AcceptsValue = true)]
    SessionId,

    /// <summary>继续上次会话</summary>
    [CliOption(JccCliArgConstants.Continue, "-c", "继续上次会话")]
    Continue,

    /// <summary>显示帮助</summary>
    [CliOption(JccCliArgConstants.Help, "-h", "显示帮助")]
    Help,
}
