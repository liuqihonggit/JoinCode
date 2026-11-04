namespace JoinCode.Abstractions.CodeIndex;

public sealed record DependencyEdge {
    /// <summary>获取源符号。</summary>
    public required string SourceSymbol { get; init; }
    /// <summary>获取目标符号。</summary>
    public required string TargetSymbol { get; init; }
    /// <summary>获取依赖类型。</summary>
    public required DependencyKind DependencyKind { get; init; }
    /// <summary>获取源文件路径。</summary>
    public string? SourceFilePath { get; init; }
}