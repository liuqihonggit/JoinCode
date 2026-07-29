namespace JoinCode.Abstractions.Models.Agent;

public sealed class MailboxMessage
{
    public required string MessageId { get; init; }
    public required string FromAgentId { get; init; }
    public required string ToAgentId { get; init; }
    public required string MessageType { get; init; }
    public required string Content { get; init; }
    public required string SessionId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}

public sealed record MailboxReadCursor
{
    public required string AgentId { get; init; }
    public required string SessionId { get; init; }
    public int LastReadLineIndex { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

public sealed class MailboxSendRequest
{
    public required string FromAgentId { get; init; }
    public required string ToAgentId { get; init; }
    public required string MessageType { get; init; }
    public required string Content { get; init; }
    public required string SessionId { get; init; }
}
