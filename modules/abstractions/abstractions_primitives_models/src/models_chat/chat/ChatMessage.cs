namespace JoinCode.Abstractions.Models.Chat;

/// <summary>
/// 聊天消息基类 — 提取 ApiMessageDocument、AgentMessage、SessionMessage 共同的 Role + Content + Timestamp 模式
/// </summary>
public abstract class ChatMessage {
    /// <summary>获取或设置消息角色。</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>获取或设置消息内容。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>获取或设置消息时间戳。</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
