namespace JoinCode.Abstractions.CodeIndex;

public sealed record IndexStats {
    /// <summary>获取索引文件数。</summary>
    public required int FileCount { get; init; }
    /// <summary>获取索引符号数。</summary>
    public required int SymbolCount { get; init; }
    /// <summary>获取调用边数。</summary>
    public required int CallEdgeCount { get; init; }
    /// <summary>获取依赖边数。</summary>
    public required int DependencyEdgeCount { get; init; }
    /// <summary>获取项目数。</summary>
    public required int ProjectCount { get; init; }
    /// <summary>获取最近更新时间。</summary>
    public required DateTimeOffset LastUpdated { get; init; }
}
