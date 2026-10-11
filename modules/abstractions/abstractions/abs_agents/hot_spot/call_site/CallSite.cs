namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 调用点 — 某符号在代码库中被引用的位置
/// </summary>
public sealed record CodeCallSite {
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取行号。</summary>
    public required int LineNumber { get; init; }
    /// <summary>获取行内容。</summary>
    public required string LineContent { get; init; }
    /// <summary>获取匹配类型。</summary>
    public required string MatchType { get; init; }
}
