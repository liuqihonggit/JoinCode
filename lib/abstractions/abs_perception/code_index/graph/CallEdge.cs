namespace JoinCode.Abstractions.CodeIndex;

public sealed record CallEdge
{
    public required string CallerSymbol { get; init; }
    public required string CalleeSymbol { get; init; }
    public required string CallSiteFilePath { get; init; }
    public required int CallSiteLine { get; init; }
    public required CallKind CallKind { get; init; }
}
