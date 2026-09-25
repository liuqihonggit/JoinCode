namespace Core.Agents.Coordinator;

/// <summary>
/// 聊天室状态持久化 DTO — 单房间序列化格式，用于 IChatRoomStore 按需加载 — ADR 0109 决策13。
/// </summary>
public sealed class ChatRoomStateData {
    /// <summary>团队/聊天室信息</summary>
    public TeamInfo Info { get; set; } = null!;

    /// <summary>成员 ID 列表</summary>
    public List<string> Members { get; set; } = [];

    /// <summary>消息列表</summary>
    public List<TeamMessage> Messages { get; set; } = [];

    /// <summary>会话 ID</summary>
    public string? SessionId { get; set; }

    /// <summary>允许路径列表</summary>
    public List<TeamAllowedPath> AllowedPaths { get; set; } = [];

    /// <summary>成员详情列表</summary>
    public List<TeamMemberInfo> MemberDetails { get; set; } = [];

    /// <summary>最大消息保留数</summary>
    public int MaxMessageCount { get; set; } = 1000;

    /// <summary>从 ChatRoomState 创建可序列化 DTO — 消息列表按时间排序从 Messages 读取</summary>
    public static ChatRoomStateData FromState(ChatRoomState state) => new() {
        Info = state.Info,
        Members = [.. state.Members],
        Messages = [.. state.Messages.Values.OrderBy(static m => m.Timestamp)],
        SessionId = state.SessionId,
        AllowedPaths = [.. state.AllowedPaths.Values],
        MemberDetails = [.. state.MemberDetails.Values],
        MaxMessageCount = state.MaxMessageCount,
    };

    /// <summary>从 DTO 恢复 ChatRoomState（不可变集合 + WithMessages 构建消息字典）</summary>
    public ChatRoomState ToState() {
        var state = new ChatRoomState {
            Info = Info,
            Members = Members.ToImmutableHashSet(),
            SessionId = SessionId,
            AllowedPaths = AllowedPaths.ToImmutableDictionary(p => p.Path),
            MemberDetails = MemberDetails.ToImmutableDictionary(m => m.AgentId),
            MaxMessageCount = MaxMessageCount,
        };
        return state.WithMessages(Messages);
    }
}
