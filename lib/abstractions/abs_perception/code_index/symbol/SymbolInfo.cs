
namespace JoinCode.Abstractions.CodeIndex;

public sealed record SymbolInfo
{
    public required string Name { get; init; }
    public required string FullyQualifiedName { get; init; }
    public required SymbolKind Kind { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int StartColumn { get; init; }
    public required int EndColumn { get; init; }
    public string? ParentSymbol { get; init; }
    public string? Namespace { get; init; }
    public string? Accessibility { get; init; }
}
