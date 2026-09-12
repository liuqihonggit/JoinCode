
namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// 节点间冲突消息队列 — 主管入队，Agent 完成后拉取，不打断 Agent 执行。
/// 对齐文档 NonBlockingMessageQueue，复用 Channel 模式。
/// </summary>
public interface IGoalConflictMessenger
{
    /// <summary>向目标节点入队冲突消息</summary>
    ValueTask EnqueueConflictAsync(ConflictMessage message, CancellationToken cancellationToken = default);

    /// <summary>拉取指定节点的所有待处理冲突消息（拉取后清空队列）</summary>
    ValueTask<IReadOnlyList<ConflictMessage>> DequeueConflictsAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>获取指定节点的待处理冲突数量（不清空队列）</summary>
    int GetPendingCount(string nodeId);
}
