
namespace Core.Context.Collapse;

public sealed class ContextCollapseResult
{
    public required bool Collapsed { get; init; }
    public required string CollapsedContent { get; init; }
    public required int OriginalTokenCount { get; init; }
    public required int CollapsedTokenCount { get; init; }
    public required int SegmentsCollapsed { get; init; }
    public required int SegmentsPreserved { get; init; }
    public required CollapseStrategy Strategy { get; init; }
    public IReadOnlyList<CollapsedSegmentInfo> CollapsedSegments { get; init; } = Array.Empty<CollapsedSegmentInfo>();
    public string? ErrorMessage { get; init; }
    public double CompressionRatio => OriginalTokenCount > 0
        ? (double)CollapsedTokenCount / OriginalTokenCount
        : 1.0;
    public int SavedTokens => OriginalTokenCount - CollapsedTokenCount;
}

public sealed class CollapsedSegmentInfo
{
    public required string SegmentId { get; init; }
    public required CollapsibleSegmentType Type { get; init; }
    public required int OriginalTokenCount { get; init; }
    public required int SummaryTokenCount { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> PreservedReferences { get; init; } = Array.Empty<string>();
}
