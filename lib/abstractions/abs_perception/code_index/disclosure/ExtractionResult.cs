namespace JoinCode.Abstractions.CodeIndex;

public sealed class ExtractionResult {
    /// <summary>获取提取的符号列表。</summary>
    public required IReadOnlyList<SymbolInfo> Symbols { get; init; }
    /// <summary>获取调用边列表。</summary>
    public required IReadOnlyList<CallEdge> Calls { get; init; }
    /// <summary>获取依赖边列表。</summary>
    public required IReadOnlyList<DependencyEdge> Dependencies { get; init; }
}