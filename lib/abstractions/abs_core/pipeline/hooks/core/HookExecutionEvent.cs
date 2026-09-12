namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 钩子执行事件类型
/// </summary>
public enum HookExecutionEventType
{
    /// <summary>钩子开始执行</summary>
    [EnumValue("started")] Started,

    /// <summary>钩子执行进度更新</summary>
    [EnumValue("progress")] Progress,

    /// <summary>钩子执行完成</summary>
    [EnumValue("response")] Response
}

/// <summary>
/// 钩子执行事件基类
/// </summary>
public abstract record HookExecutionEvent
{
    /// <summary>
    /// 事件类型
    /// </summary>
    public abstract HookExecutionEventType EventType { get; }

    /// <summary>
    /// 钩子ID
    /// </summary>
    public required string HookId { get; init; }

    /// <summary>
    /// 钩子名称
    /// </summary>
    public required string HookName { get; init; }

    /// <summary>
    /// 钩子事件
    /// </summary>
    public required HookEvent HookEvent { get; init; }
}

/// <summary>
/// 钩子开始执行事件
/// </summary>
public sealed record HookStartedEvent : HookExecutionEvent
{
    public override HookExecutionEventType EventType => HookExecutionEventType.Started;
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 钩子进度事件
/// </summary>
public sealed record HookProgressEvent : HookExecutionEvent
{
    public override HookExecutionEventType EventType => HookExecutionEventType.Progress;
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public string? Output => $"{Stdout}{Stderr}";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 钩子响应事件
/// </summary>
public sealed record HookResponseEvent : HookExecutionEvent
{
    public override HookExecutionEventType EventType => HookExecutionEventType.Response;
    public string? Output { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public int? ExitCode { get; init; }
    public required HookExecutionOutcome Outcome { get; init; }
    public DateTimeOffset EndTime { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// 钩子执行结果（用于事件）
/// </summary>
public enum HookExecutionOutcome
{
    /// <summary>成功</summary>
    Success,

    /// <summary>错误</summary>
    Error,

    /// <summary>已取消</summary>
    Cancelled
}
