
namespace Core.Summary;

/// <summary>
/// 离开摘要生成结果
/// </summary>
public sealed class AwaySummaryResult {
    /// <summary>
    /// 获取一个值，指示摘要生成是否成功。
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 获取生成的摘要文本。
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// 获取用户离开时刻。
    /// </summary>
    public required DateTime AwayTime { get; init; }

    /// <summary>
    /// 获取用户返回时刻（摘要生成时刻）。
    /// </summary>
    public required DateTime ReturnTime { get; init; }

    /// <summary>
    /// 获取离开持续时长。
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// 获取离开期间跟踪的事件总数。
    /// </summary>
    public required int TotalEvents { get; init; }

    /// <summary>
    /// 获取离开期间工具调用次数。
    /// </summary>
    public required int ToolCallCount { get; init; }

    /// <summary>
    /// 获取离开期间消息条数。
    /// </summary>
    public required int MessageCount { get; init; }

    /// <summary>
    /// 获取离开期间错误个数。
    /// </summary>
    public required int ErrorCount { get; init; }

    /// <summary>
    /// 获取关键事件列表；默认为空数组。
    /// </summary>
    public IReadOnlyList<AwayEvent> KeyEvents { get; init; } = Array.Empty<AwayEvent>();

    /// <summary>
    /// 获取错误事件列表；默认为空数组。
    /// </summary>
    public IReadOnlyList<AwayEvent> Errors { get; init; } = Array.Empty<AwayEvent>();

    /// <summary>
    /// 获取错误消息；摘要成功时为 <c>null</c>。
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 离开事件 — 记录用户离开期间发生的单个事件
/// </summary>
public sealed class AwayEvent {
    /// <summary>
    /// 获取事件发生的时间戳。
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// 获取事件类型。
    /// </summary>
    public required AwayEventType Type { get; init; }

    /// <summary>
    /// 获取事件描述文本。
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 获取事件元数据字典；默认为空字典。
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// 离开事件类型枚举
/// </summary>
public enum AwayEventType {
    /// <summary>
    /// 工具调用事件。
    /// </summary>
    [EnumValue("toolCall")] ToolCall,

    /// <summary>
    /// 消息事件。
    /// </summary>
    [EnumValue("message")] Message,

    /// <summary>
    /// 错误事件。
    /// </summary>
    [EnumValue("error")] Error,

    /// <summary>
    /// 状态变更事件。
    /// </summary>
    [EnumValue("stateChange")] StateChange,

    /// <summary>
    /// 通知事件。
    /// </summary>
    [EnumValue("notification")] Notification
}