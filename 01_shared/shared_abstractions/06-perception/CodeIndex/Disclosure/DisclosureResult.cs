namespace JoinCode.Abstractions.CodeIndex;

public sealed record DisclosureResult
{
    public required string Query { get; init; }
    public required DisclosureLevel Level { get; init; }
    public required string FormattedContent { get; init; }
    public required IReadOnlyList<SymbolInfo> Symbols { get; init; }
    public IReadOnlyList<CallEdge>? Callers { get; init; }
    public IReadOnlyList<CallEdge>? Callees { get; init; }
    public IReadOnlyList<DependencyEdge>? Inheritors { get; init; }
    public IReadOnlyList<DependencyEdge>? Dependencies { get; init; }
    public IReadOnlyList<SourceSnippet>? SourceSnippets { get; init; }
    public required int EstimatedTokens { get; init; }
    public bool HasMoreDetails => Level < DisclosureLevel.Source && Symbols.Count > 0;
}

public sealed record SourceSnippet
{
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }
    public required string SymbolName { get; init; }
}
