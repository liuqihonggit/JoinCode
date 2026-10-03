namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 聊天室成员视图模型 — 对标 QQ 群成员头像，显示首字母圆形头像+悬浮提示。
/// 从 <see cref="JoinCode.Abstractions.Models.Agent.ChatRoomMember"/> 映射而来。
/// </summary>
public sealed class ChatRoomMemberVm {
    /// <summary>成员显示名（bot 中文名或用户指定名）</summary>
    public required string DisplayName { get; init; }

    /// <summary>头像首字母（DisplayName 首字符大写）</summary>
    public required string Initial { get; init; }

    /// <summary>头像背景色（#RRGGBB 格式，按 AgentId 哈希分配品牌色）</summary>
    public required string AvatarColor { get; init; }

    /// <summary>是否在线（离线时头像半透明）</summary>
    public bool IsOnline { get; init; } = true;
}

/// <summary>
/// 聊天室消息视图模型 — 对标 QQ 群消息气泡，显示发送者名+时间+内容。
/// 从 <see cref="JoinCode.Abstractions.Models.Agent.TeamMessage"/> 映射而来。
/// </summary>
public sealed class ChatRoomMessageVm {
    /// <summary>发送者显示名</summary>
    public required string SenderName { get; init; }

    /// <summary>发送者名称颜色（按 SenderId 哈希分配，区分不同 bot）</summary>
    public required string SenderColor { get; init; }

    /// <summary>消息内容</summary>
    public required string Content { get; init; }

    /// <summary>时间显示（HH:mm 格式）</summary>
    public required string TimeDisplay { get; init; }
}
