namespace JoinCode.Abstractions.CodeIndex;

public sealed record CallEdge {
    /// <summary>获取调用方符号。</summary>
    public required string CallerSymbol { get; init; }
    /// <summary>获取被调用方符号。</summary>
    public required string CalleeSymbol { get; init; }
    /// <summary>获取调用点文件路径。</summary>
    public required string CallSiteFilePath { get; init; }
    /// <summary>获取调用点行号。</summary>
    public required int CallSiteLine { get; init; }
    /// <summary>获取调用类型。</summary>
    public required CallKind CallKind { get; init; }
}