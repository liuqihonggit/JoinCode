namespace JoinCode.Abstractions.Models.Chat;

/// <summary>
/// 聊天消息基类 — 提取 ApiMessageDocument、AgentMessage、SessionMessage 共同的 Role + Content + Timestamp 模式
/// </summary>
public abstract class ChatMessage
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
