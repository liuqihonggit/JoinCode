namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 社区信息 — 标签传播算法的输出
/// </summary>
public sealed record CommunityInfo
{
    public required int CommunityId { get; init; }
    public required IReadOnlyList<string> Members { get; init; }
    public required int MemberCount { get; init; }
    public required int InternalEdges { get; init; }
    public required int ExternalEdges { get; init; }
}
