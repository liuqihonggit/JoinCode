
namespace JoinCode.Abstractions.Models.Telemetry;

public sealed class TelemetrySpanData {
    /// <summary>获取 Span 名称。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>获取 Span 标识。</summary>
    public string SpanId { get; init; } = string.Empty;
    /// <summary>获取 Trace 标识。</summary>
    public string TraceId { get; init; } = string.Empty;
    /// <summary>获取父 Span 标识。</summary>
    public string? ParentSpanId { get; init; }
    /// <summary>获取 Span 类型。</summary>
    public TelemetrySpanKind Kind { get; init; }
    /// <summary>获取状态码。</summary>
    public TelemetryStatusCode Status { get; init; }
    /// <summary>获取状态描述。</summary>
    public string? StatusDescription { get; init; }
    /// <summary>获取开始时间。</summary>
    public DateTimeOffset StartTime { get; init; }
    /// <summary>获取结束时间。</summary>
    public DateTimeOffset EndTime { get; init; }
    /// <summary>获取持续时间。</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>获取标签字典。</summary>
    public Dictionary<string, string> Tags { get; init; } = [];
    /// <summary>获取事件列表。</summary>
    public List<TelemetrySpanEvent> Events { get; init; } = [];
}

public sealed class TelemetrySpanEvent {
    /// <summary>获取事件名称。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>获取时间戳。</summary>
    public DateTimeOffset Timestamp { get; init; }
    /// <summary>获取标签字典。</summary>
    public Dictionary<string, string> Tags { get; init; } = [];
}