
namespace JoinCode.Abstractions.Models.Telemetry;

public sealed class TelemetrySpanData
{
    public string Name { get; init; } = string.Empty;
    public string SpanId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string? ParentSpanId { get; init; }
    public TelemetrySpanKind Kind { get; init; }
    public TelemetryStatusCode Status { get; init; }
    public string? StatusDescription { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
    public List<TelemetrySpanEvent> Events { get; init; } = [];
}

public sealed class TelemetrySpanEvent
{
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public Dictionary<string, string> Tags { get; init; } = [];
}
