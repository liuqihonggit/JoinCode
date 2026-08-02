namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 统一 Agent 接口 — 合并原 IAgent（对话）和 ISubAgent（执行/控制）
/// mainAgent 和 subAgent 共用此接口
/// </summary>
public interface ISubAgent : IAgent, IDisposable
{
    string Id { get; }
    string Task { get; }
    SubAgentOptions Options { get; }
    SubAgentContext? Context { get; }
    TaskExecutionStatus Status { get; set; }
    TaskExecutionStatus State { get; set; }
    DateTime CreatedAt { get; }
    DateTime? StartedAt { get; set; }
    DateTime? CompletedAt { get; }
    CancellationTokenSource? CancellationTokenSource { get; set; }
    void AddContext(string context);
    Task<SubAgentResult> ExecuteAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentStreamChunk> ExecuteStreamAsync(CancellationToken cancellationToken = default);
    void Pause();
    void Resume();
    void Cancel();
    void Reset();
}
