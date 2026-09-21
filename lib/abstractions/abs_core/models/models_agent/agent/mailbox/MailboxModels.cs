namespace JoinCode.Abstractions.Models.Agent;

// MailboxMessage 已归纳到 CoordinatorMessage，统一消息模型。
// 通过 global using MailboxMessage = CoordinatorMessage 别名保持向后兼容。

public sealed record MailboxReadCursor {
    /// <summary>获取代理标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取会话标识。</summary>
    public required string SessionId { get; init; }
    /// <summary>获取最后读取行索引。</summary>
    public int LastReadLineIndex { get; init; }
    /// <summary>获取更新时间。</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed class MailboxSendRequest {
    /// <summary>获取来源代理标识。</summary>
    public required string FromAgentId { get; init; }
    /// <summary>获取目标代理标识。</summary>
    public required string ToAgentId { get; init; }
    /// <summary>获取消息类型。</summary>
    public required string MessageType { get; init; }
    /// <summary>获取消息内容。</summary>
    public required string Content { get; init; }
    /// <summary>获取会话标识。</summary>
    public required string SessionId { get; init; }
}