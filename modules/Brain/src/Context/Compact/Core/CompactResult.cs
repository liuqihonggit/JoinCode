
namespace Core.Context.Compact;

public enum CompactTrigger
{
    Manual,
    Auto,
    Reactive
}

public enum CompactLevel
{
    [EnumValue("none")] None,
    [EnumValue("microcompact")] Microcompact,
    [EnumValue("timeBasedMicrocompact")] TimeBasedMicrocompact,
    [EnumValue("sessionMemoryCompact")] SessionMemoryCompact,
    [EnumValue("fullCompact")] FullCompact,
    [EnumValue("partialCompact")] PartialCompact,
    [EnumValue("reactiveCompact")] ReactiveCompact
}

public sealed class CompactResult
{
    public required bool Compacted { get; init; }
    public required CompactLevel Level { get; init; }
    public required CompactTrigger Trigger { get; init; }
    public string? Summary { get; init; }
    public int PreCompactTokenCount { get; init; }
    public int PostCompactTokenCount { get; init; }
    public int MessagesRemoved { get; init; }
    public int MessagesPreserved { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
    public double TokenSavingsRatio => PreCompactTokenCount > 0
        ? 1.0 - (double)PostCompactTokenCount / PreCompactTokenCount
        : 0;
}
