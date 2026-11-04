
namespace JoinCode.Abstractions.CodeIndex;

public sealed record SymbolInfo {
    /// <summary>获取符号名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取完全限定名。</summary>
    public required string FullyQualifiedName { get; init; }
    /// <summary>获取符号类型。</summary>
    public required SymbolKind Kind { get; init; }
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取起始行号。</summary>
    public required int StartLine { get; init; }
    /// <summary>获取结束行号。</summary>
    public required int EndLine { get; init; }
    /// <summary>获取起始列号。</summary>
    public required int StartColumn { get; init; }
    /// <summary>获取结束列号。</summary>
    public required int EndColumn { get; init; }
    /// <summary>获取父符号名称。</summary>
    public string? ParentSymbol { get; init; }
    /// <summary>获取命名空间。</summary>
    public string? Namespace { get; init; }
    /// <summary>获取可访问性。</summary>
    public string? Accessibility { get; init; }
}