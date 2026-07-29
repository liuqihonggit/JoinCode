namespace JoinCode.Abstractions.Models.Agent;

public sealed class CoordinatorMessage
{
    public required string FromAgentId { get; init; }
    public required string ToAgentId { get; init; }
    public required string MessageType { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public TeammateMessageType? StructuredType { get; init; }
    public string? RequestId { get; init; }
    public Dictionary<string, JsonElement>? Payload { get; init; }
    public bool IsStructured => StructuredType is not null;
}
