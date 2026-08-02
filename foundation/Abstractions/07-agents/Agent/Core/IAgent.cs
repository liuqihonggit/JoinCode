
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 通用 Agent 接口 — mainAgent 和 subAgent 共用
/// 包含全部能力：身份/执行/暂停/恢复/取消/上下文
/// 不再有 ISubAgent（语义错误：mainAgent 不是 Sub）
/// </summary>
public interface IAgent : IDisposable
{
    ObjectId ObjectId { get; }
    string Id { get; }
    string Name { get; }
    bool IsSubAgent { get; }
    ObjectId? ParentObjectId { get; }
    string? AgentType { get; }

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
