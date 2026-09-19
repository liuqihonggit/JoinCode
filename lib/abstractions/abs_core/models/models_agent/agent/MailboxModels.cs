namespace JoinCode.Abstractions.Models.Agent;

// MailboxMessage 已归纳到 CoordinatorMessage，统一消息模型。
// 通过 global using MailboxMessage = CoordinatorMessage 别名保持向后兼容。

public sealed record MailboxReadCursor {
    public required string AgentId { get; init; }
    public required string SessionId { get; init; }
    public int LastReadLineIndex { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed class MailboxSendRequest {
    public required string FromAgentId { get; init; }
    public required string ToAgentId { get; init; }
    public required string MessageType { get; init; }
    public required string Content { get; init; }
    public required string SessionId { get; init; }
}