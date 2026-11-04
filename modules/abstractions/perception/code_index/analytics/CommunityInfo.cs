namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 社区信息 — 标签传播算法的输出
/// </summary>
public sealed record CommunityInfo {
    /// <summary>获取社区标识。</summary>
    public required int CommunityId { get; init; }
    /// <summary>获取社区成员列表。</summary>
    public required IReadOnlyList<string> Members { get; init; }
    /// <summary>获取成员数量。</summary>
    public required int MemberCount { get; init; }
    /// <summary>获取内部边数。</summary>
    public required int InternalEdges { get; init; }
    /// <summary>获取外部边数。</summary>
    public required int ExternalEdges { get; init; }
}