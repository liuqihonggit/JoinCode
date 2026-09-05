namespace JoinCode.Abstractions.Utils;

/// <summary>
/// GitHub Actions 日志过滤级别 — 用于 gh_run_view 的 filter 参数，流式过滤减少返回量
/// <para>GitHub Actions 日志标记：##[error] / ##[warning] / ##[command] / ##[group]</para>
/// </summary>
public enum GitHubLogFilter
{
    /// <summary>仅错误行(含 ##[error])</summary>
    [EnumValue("error")]
    Error,

    /// <summary>错误+警告行(含 ##[error] / ##[warning])</summary>
    [EnumValue("warning")]
    Warning,

    /// <summary>错误+警告+命令行(含 ##[error] / ##[warning] / ##[command])</summary>
    [EnumValue("info")]
    Info,

    /// <summary>不过滤，返回全部日志</summary>
    [EnumValue("all")]
    All,
}
