namespace JoinCode.Abstractions.CodeIndex;

public sealed class ExtractionResult
{
    public required IReadOnlyList<SymbolInfo> Symbols { get; init; }
    public required IReadOnlyList<CallEdge> Calls { get; init; }
    public required IReadOnlyList<DependencyEdge> Dependencies { get; init; }
}
