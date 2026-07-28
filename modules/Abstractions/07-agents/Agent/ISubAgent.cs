namespace JoinCode.Abstractions.Interfaces;

public interface ISubAgent : IDisposable
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
