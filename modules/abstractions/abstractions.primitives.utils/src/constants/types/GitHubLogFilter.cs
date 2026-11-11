namespace JoinCode.Abstractions.Utils;

/// <summary>
/// GitHub Actions 日志过滤级别 — [Flags] 位标志,AI 可逗号分隔组合(如 --filter error,failed)
/// <para>每个位代表一类结构化标记,组合时按位或: error,failed = Error|Failed</para>
/// <para>None=不过滤返回全部; All=所有标记都匹配</para>
/// </summary>
[Flags]
public enum GitHubLogFilter {
    /// <summary>不过滤,返回全部日志</summary>
    None = 0,

    /// <summary>##[error] — GitHub Actions 错误标记</summary>
    [EnumValue("error")]
    Error = 1,

    /// <summary>##[warning] — GitHub Actions 警告标记</summary>
    [EnumValue("warning")]
    Warning = 2,

    /// <summary>##[command] — GitHub Actions 命令标记</summary>
    [EnumValue("command")]
    Command = 4,

    /// <summary>[FAIL] / "  Failed " — 测试失败标记</summary>
    [EnumValue("failed")]
    Failed = 8,

    /// <summary>Exception: — 异常抛出(带冒号,避免匹配测试名)</summary>
    [EnumValue("exception")]
    Exception = 16,

    /// <summary>error+warning+command 组合(向后兼容)</summary>
    [EnumValue("info")]
    Info = Error | Warning | Command,

    /// <summary>所有标记都匹配</summary>
    [EnumValue("all")]
    All = Error | Warning | Command | Failed | Exception,
}
