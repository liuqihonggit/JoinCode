
namespace JoinCode.Abstractions.Interfaces;

using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 通用 Agent 接口 — 协调者和执行者共用
/// 身份信息通过 ObjectId 获取（ObjectId.SequenceId / ObjectId.UniqueId / ObjectId.DisplayName）
/// 角色通过 Role 属性区分：Coordinator（协调者）或 Executor（执行者）
/// 只声明行为能力：执行/暂停/恢复/取消/重置
/// </summary>
public interface IAgent : IDisposable
{
    ObjectId ObjectId { get; }
    string Name { get; }
    AgentRole Role { get; }
    ExecutorVariant? Variant { get; }
    ObjectId? ParentObjectId { get; }

    string Task { get; }
    TaskExecutionStatus Status { get; set; }
    MessageList ChatHistory { get; }
    string? SystemPrompt { get; }
    string? Instruction { get; set; }

    Task<SubAgentResult> ExecuteAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentStreamChunk> ExecuteStreamAsync(CancellationToken cancellationToken = default);
    void Pause();
    void Resume();
    void Cancel();
    void Reset();
}
