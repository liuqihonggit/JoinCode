namespace JoinCode.Abstractions.CodeIndex;

public sealed record IndexStats
{
    public required int FileCount { get; init; }
    public required int SymbolCount { get; init; }
    public required int CallEdgeCount { get; init; }
    public required int DependencyEdgeCount { get; init; }
    public required int ProjectCount { get; init; }
    public required DateTimeOffset LastUpdated { get; init; }
}
