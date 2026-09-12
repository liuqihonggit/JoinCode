namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 节点间冲突消息 — 主管入队，Agent 完成后拉取。
/// </summary>
public sealed class ConflictMessage
{
    public required string SourceNodeId { get; init; }
    public required string TargetNodeId { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
