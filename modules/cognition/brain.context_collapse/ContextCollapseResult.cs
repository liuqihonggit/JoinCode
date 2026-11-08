
namespace Core.Context.Collapse;

/// <summary>
/// 上下文折叠结果
/// </summary>
public sealed class ContextCollapseResult {
    /// <summary>
    /// 是否发生了折叠
    /// </summary>
    public required bool Collapsed { get; init; }
    /// <summary>
    /// 折叠后的内容
    /// </summary>
    public required string CollapsedContent { get; init; }
    /// <summary>
    /// 原始 token 数
    /// </summary>
    public required int OriginalTokenCount { get; init; }
    /// <summary>
    /// 折叠后 token 数
    /// </summary>
    public required int CollapsedTokenCount { get; init; }
    /// <summary>
    /// 已折叠的段数
    /// </summary>
    public required int SegmentsCollapsed { get; init; }
    /// <summary>
    /// 保留未折叠的段数
    /// </summary>
    public required int SegmentsPreserved { get; init; }
    /// <summary>
    /// 使用的折叠策略
    /// </summary>
    public required CollapseStrategy Strategy { get; init; }
    /// <summary>
    /// 各折叠段的详细信息
    /// </summary>
    public IReadOnlyList<CollapsedSegmentInfo> CollapsedSegments { get; init; } = Array.Empty<CollapsedSegmentInfo>();
    /// <summary>
    /// 错误信息（折叠失败时填充）
    /// </summary>
    public string? ErrorMessage { get; init; }
    /// <summary>
    /// 压缩比（折叠后 token 数 / 原始 token 数）
    /// </summary>
    public double CompressionRatio => OriginalTokenCount > 0
        ? (double)CollapsedTokenCount / OriginalTokenCount
        : 1.0;
    /// <summary>
    /// 节省的 token 数
    /// </summary>
    public int SavedTokens => OriginalTokenCount - CollapsedTokenCount;
}

/// <summary>
/// 单个折叠段的详细信息
/// </summary>
public sealed class CollapsedSegmentInfo {
    /// <summary>
    /// 段唯一标识
    /// </summary>
    public required string SegmentId { get; init; }
    /// <summary>
    /// 段类型
    /// </summary>
    public required CollapsibleSegmentType Type { get; init; }
    /// <summary>
    /// 段原始 token 数
    /// </summary>
    public required int OriginalTokenCount { get; init; }
    /// <summary>
    /// 摘要 token 数
    /// </summary>
    public required int SummaryTokenCount { get; init; }
    /// <summary>
    /// 生成的摘要文本
    /// </summary>
    public required string Summary { get; init; }
    /// <summary>
    /// 保留的关键引用
    /// </summary>
    public IReadOnlyList<string> PreservedReferences { get; init; } = Array.Empty<string>();
}