namespace JoinCode.Abstractions.CodeIndex;

public sealed record DependencyEdge
{
    public required string SourceSymbol { get; init; }
    public required string TargetSymbol { get; init; }
    public required DependencyKind DependencyKind { get; init; }
    public string? SourceFilePath { get; init; }
}
