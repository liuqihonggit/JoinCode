
namespace Core.Context.Collapse;

public sealed class CollapsibleSegment
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required CollapsibleSegmentType Type { get; init; }
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public int TokenCount { get; init; }
    public double CollapsePriority { get; init; }
    public IReadOnlyList<string> KeyReferences { get; init; } = Array.Empty<string>();
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public enum CollapsibleSegmentType
{
    [EnumValue("codeBlock")] CodeBlock,
    [EnumValue("repetitivePattern")] RepetitivePattern,
    [EnumValue("historicalDialogue")] HistoricalDialogue,
    [EnumValue("toolOutput")] ToolOutput,
    [EnumValue("longProse")] LongProse,
    [EnumValue("systemMessage")] SystemMessage
}
