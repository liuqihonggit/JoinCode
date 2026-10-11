namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 钩子执行事件类型
/// </summary>
public enum HookExecutionEventType {
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
public abstract record HookExecutionEvent {
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
public sealed record HookStartedEvent : HookExecutionEvent {
    /// <summary>获取事件类型。</summary>
    public override HookExecutionEventType EventType => HookExecutionEventType.Started;
    /// <summary>获取开始时间。</summary>
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 钩子进度事件
/// </summary>
public sealed record HookProgressEvent : HookExecutionEvent {
    /// <summary>获取事件类型。</summary>
    public override HookExecutionEventType EventType => HookExecutionEventType.Progress;
    /// <summary>获取标准输出。</summary>
    public string? Stdout { get; init; }
    /// <summary>获取标准错误。</summary>
    public string? Stderr { get; init; }
    /// <summary>获取合并输出。</summary>
    public string? Output => $"{Stdout}{Stderr}";
    /// <summary>获取时间戳。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 钩子响应事件
/// </summary>
public sealed record HookResponseEvent : HookExecutionEvent {
    /// <summary>获取事件类型。</summary>
    public override HookExecutionEventType EventType => HookExecutionEventType.Response;
    /// <summary>获取合并输出。</summary>
    public string? Output { get; init; }
    /// <summary>获取标准输出。</summary>
    public string? Stdout { get; init; }
    /// <summary>获取标准错误。</summary>
    public string? Stderr { get; init; }
    /// <summary>获取退出码。</summary>
    public int? ExitCode { get; init; }
    /// <summary>获取执行结果。</summary>
    public required HookExecutionOutcome Outcome { get; init; }
    /// <summary>获取结束时间。</summary>
    public DateTimeOffset EndTime { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>获取执行时长。</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// 钩子执行结果（用于事件）
/// </summary>
public enum HookExecutionOutcome {
    /// <summary>成功</summary>
    [EnumValue("success")]
    Success,

    /// <summary>错误</summary>
    [EnumValue("error")]
    Error,

    /// <summary>已取消</summary>
    [EnumValue("cancelled")]
    Cancelled,
}