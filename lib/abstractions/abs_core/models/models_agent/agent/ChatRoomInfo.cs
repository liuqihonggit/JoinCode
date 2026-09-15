namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 聊天室信息 — 团队的聊天室视图，包含房间名和成员显示名列表。
/// 用于子代理查询当前聊天室状态 — ADR 0109。
/// </summary>
public sealed record ChatRoomInfo
{
    /// <summary>聊天室名称（等同团队名）</summary>
    public required string RoomName { get; init; }

    /// <summary>聊天室成员显示名列表</summary>
    public required IReadOnlyList<string> Members { get; init; }

    /// <summary>成员数量</summary>
    public int MemberCount => Members.Count;
}
