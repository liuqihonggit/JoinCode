namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 节点间冲突消息 — 主管入队，Agent 完成后拉取。
/// </summary>
public sealed class ConflictMessage {
    /// <summary>获取源节点标识。</summary>
    public required string SourceNodeId { get; init; }
    /// <summary>获取目标节点标识。</summary>
    public required string TargetNodeId { get; init; }
    /// <summary>获取消息内容。</summary>
    public required string Content { get; init; }
    /// <summary>获取创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}