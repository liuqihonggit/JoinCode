namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 聊天室 partial — 从引擎拉取子代理列表和活动，
/// 映射为聊天室成员和消息，驱动 ChatRoomView 显示。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>头像颜色调色板（按 AgentId 哈希分配）</summary>
    private static readonly string[] ChatRoomAvatarColors =
        ["#4A90D9", "#E74C3C", "#2ECC71", "#F39C12", "#9B59B6", "#1ABC9C", "#34495E", "#E67E22"];

    /// <summary>刷新聊天室数据 — 从引擎拉取子代理列表和活动</summary>
    [RelayCommand]
    private async Task RefreshChatRoomAsync() {
        var agents = await _session.GetBackgroundAgentsAsync();
        var members = agents.Select(MapAgentToMember).ToList();
        var messages = agents.SelectMany(MapAgentToMessages).ToList();
        RefreshChatRoom(members, messages);
    }

    /// <summary>把子代理映射为聊天室成员</summary>
    private static ChatRoomMemberVm MapAgentToMember(BackgroundAgentInfo agent) => new() {
        DisplayName = agent.Name,
        Initial = string.IsNullOrEmpty(agent.Name) ? "?" : char.ToUpperInvariant(agent.Name[0]).ToString(),
        AvatarColor = PickAvatarColor(agent.AgentId),
        IsOnline = agent.State == AgentStatus.Running,
    };

    /// <summary>把子代理活动映射为聊天室消息</summary>
    private static IEnumerable<ChatRoomMessageVm> MapAgentToMessages(BackgroundAgentInfo agent) {
        var color = PickAvatarColor(agent.AgentId);
        return agent.Activities.Select(static (a, _) => a).Select(a => new ChatRoomMessageVm {
            SenderName = agent.Name,
            SenderColor = color,
            Content = a.Text,
            TimeDisplay = a.Timestamp.ToString("HH:mm"),
        });
    }

    /// <summary>按 ID 哈希分配头像颜色</summary>
    private static string PickAvatarColor(string id) {
        var hash = id.GetHashCode();
        return ChatRoomAvatarColors[Math.Abs(hash) % ChatRoomAvatarColors.Length];
    }
}
