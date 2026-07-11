
namespace Core.Summary;

public sealed class AwaySummaryResult
{
    public required bool Success { get; init; }
    public required string Summary { get; init; }
    public required DateTime AwayTime { get; init; }
    public required DateTime ReturnTime { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int TotalEvents { get; init; }
    public required int ToolCallCount { get; init; }
    public required int MessageCount { get; init; }
    public required int ErrorCount { get; init; }
    public IReadOnlyList<AwayEvent> KeyEvents { get; init; } = Array.Empty<AwayEvent>();
    public IReadOnlyList<AwayEvent> Errors { get; init; } = Array.Empty<AwayEvent>();
    public string? ErrorMessage { get; init; }
}

public sealed class AwayEvent
{
    public required DateTime Timestamp { get; init; }
    public required AwayEventType Type { get; init; }
    public required string Description { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public enum AwayEventType
{
    [EnumValue("toolCall")] ToolCall,
    [EnumValue("message")] Message,
    [EnumValue("error")] Error,
    [EnumValue("stateChange")] StateChange,
    [EnumValue("notification")] Notification
}
