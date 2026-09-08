namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 文件追踪信息 — 用于持久化 FileTracking 字典的条目
/// </summary>
public sealed record FileTrackingInfo
{
    public required string FilePath { get; init; }
    public required string Hash { get; init; }
    public required int SymbolCount { get; init; }
    public required DateTimeOffset LastModified { get; init; }
}
