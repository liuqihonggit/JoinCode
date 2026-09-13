
namespace Core.Context.Collapse;

/// <summary>
/// 上下文折叠选项
/// </summary>
public sealed class ContextCollapseOptions
{
    /// <summary>
    /// 折叠策略
    /// </summary>
    public CollapseStrategy Strategy { get; init; } = CollapseStrategy.Balanced;
    /// <summary>
    /// 最多折叠的段数
    /// </summary>
    public int MaxSegmentsToCollapse { get; init; } = 10;
    /// <summary>
    /// 可折叠段的最小 token 数
    /// </summary>
    public int MinSegmentTokenCount { get; init; } = 100;
    /// <summary>
    /// 可折叠段的最小优先级
    /// </summary>
    public double MinCollapsePriority { get; init; } = 0.3;
    /// <summary>
    /// 是否保留关键引用
    /// </summary>
    public bool PreserveKeyReferences { get; init; } = true;
    /// <summary>
    /// 摘要最大长度
    /// </summary>
    public int MaxSummaryLength { get; init; } = 200;
    /// <summary>
    /// 目标压缩比
    /// </summary>
    public double TargetCompressionRatio { get; init; } = 0.5;

    /// <summary>
    /// 激进策略预设选项
    /// </summary>
    public static ContextCollapseOptions Aggressive => new()
    {
        Strategy = CollapseStrategy.Aggressive,
        MinSegmentTokenCount = 50,
        MinCollapsePriority = 0.2,
        TargetCompressionRatio = 0.3,
        MaxSummaryLength = 100
    };

    /// <summary>
    /// 平衡策略预设选项
    /// </summary>
    public static ContextCollapseOptions Balanced => new();

    /// <summary>
    /// 保守策略预设选项
    /// </summary>
    public static ContextCollapseOptions Conservative => new()
    {
        Strategy = CollapseStrategy.Conservative,
        MinSegmentTokenCount = 200,
        MinCollapsePriority = 0.5,
        TargetCompressionRatio = 0.7,
        MaxSummaryLength = 300
    };
}
