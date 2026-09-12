namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具超时策略 — 由工具处理组基类定义，源码生成器读取继承链注入到 IToolHandler.TimeoutPolicy
/// </summary>
public sealed record ToolTimeoutPolicy
{
    /// <summary>绝对超时秒数，null=无限制</summary>
    public int? AbsoluteTimeoutSeconds { get; init; }

    /// <summary>是否支持续期（resume/continue/stop 工具可用）</summary>
    public bool SupportsResume { get; init; }

    /// <summary>超时是否 kill 进程（false=仅返回超时状态，不 kill）</summary>
    public bool KillOnTimeout { get; init; }

    /// <summary>无限制 — 长期运行工具使用</summary>
    public static readonly ToolTimeoutPolicy None = new();

    /// <summary>2分钟绝对超时 + kill + 可续期 — 一次性命令使用</summary>
    public static readonly ToolTimeoutPolicy AbsoluteTwoMinutes = new()
    {
        AbsoluteTimeoutSeconds = 120,
        SupportsResume = true,
        KillOnTimeout = true,
    };
}
