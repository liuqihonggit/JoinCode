namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 文件追踪信息 — 用于持久化 FileTracking 字典的条目
/// </summary>
public sealed record FileTrackingInfo {
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取文件哈希。</summary>
    public required string Hash { get; init; }
    /// <summary>获取符号数。</summary>
    public required int SymbolCount { get; init; }
    /// <summary>获取最后修改时间。</summary>
    public required DateTimeOffset LastModified { get; init; }
}