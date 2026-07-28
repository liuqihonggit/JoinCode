
namespace Core.Context.Collapse;

public sealed class ContextCollapseOptions
{
    public CollapseStrategy Strategy { get; init; } = CollapseStrategy.Balanced;
    public int MaxSegmentsToCollapse { get; init; } = 10;
    public int MinSegmentTokenCount { get; init; } = 100;
    public double MinCollapsePriority { get; init; } = 0.3;
    public bool PreserveKeyReferences { get; init; } = true;
    public int MaxSummaryLength { get; init; } = 200;
    public double TargetCompressionRatio { get; init; } = 0.5;

    public static ContextCollapseOptions Aggressive => new()
    {
        Strategy = CollapseStrategy.Aggressive,
        MinSegmentTokenCount = 50,
        MinCollapsePriority = 0.2,
        TargetCompressionRatio = 0.3,
        MaxSummaryLength = 100
    };

    public static ContextCollapseOptions Balanced => new();

    public static ContextCollapseOptions Conservative => new()
    {
        Strategy = CollapseStrategy.Conservative,
        MinSegmentTokenCount = 200,
        MinCollapsePriority = 0.5,
        TargetCompressionRatio = 0.7,
        MaxSummaryLength = 300
    };
}
