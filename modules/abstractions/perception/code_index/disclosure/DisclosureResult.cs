namespace JoinCode.Abstractions.CodeIndex;

/// <summary>代码披露结果。</summary>
public sealed record DisclosureResult {
    /// <summary>获取查询字符串。</summary>
    public required string Query { get; init; }
    /// <summary>获取披露级别。</summary>
    public required DisclosureLevel Level { get; init; }
    /// <summary>获取格式化后的内容。</summary>
    public required string FormattedContent { get; init; }
    /// <summary>获取符号信息列表。</summary>
    public required IReadOnlyList<SymbolInfo> Symbols { get; init; }
    /// <summary>获取调用方边列表。</summary>
    public IReadOnlyList<CallEdge>? Callers { get; init; }
    /// <summary>获取被调用方边列表。</summary>
    public IReadOnlyList<CallEdge>? Callees { get; init; }
    /// <summary>获取继承者边列表。</summary>
    public IReadOnlyList<DependencyEdge>? Inheritors { get; init; }
    /// <summary>获取依赖边列表。</summary>
    public IReadOnlyList<DependencyEdge>? Dependencies { get; init; }
    /// <summary>获取源代码片段列表。</summary>
    public IReadOnlyList<SourceSnippet>? SourceSnippets { get; init; }
    /// <summary>获取估算的 token 数。</summary>
    public required int EstimatedTokens { get; init; }
    /// <summary>获取是否存在更多可披露的细节。</summary>
    public bool HasMoreDetails => Level < DisclosureLevel.Source && Symbols.Count > 0;
}

/// <summary>源代码片段。</summary>
public sealed record SourceSnippet {
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取起始行号。</summary>
    public required int StartLine { get; init; }
    /// <summary>获取结束行号。</summary>
    public required int EndLine { get; init; }
    /// <summary>获取片段内容。</summary>
    public required string Content { get; init; }
    /// <summary>获取所属符号名称。</summary>
    public required string SymbolName { get; init; }
}
