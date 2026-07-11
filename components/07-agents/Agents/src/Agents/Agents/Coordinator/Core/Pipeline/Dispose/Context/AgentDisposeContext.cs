namespace Core.Agents.Coordinator;

public sealed class AgentDisposeContext : IPipelineContext
{
    public required string AgentId { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public WorktreeCleanupDetail? WorktreeCleanupResult { get; set; }
    public int CancelledShellTaskCount { get; set; }
    public bool LifecycleDisposed { get; set; }
    public bool ExecutionContextRemoved { get; set; }
    public bool StartTimeRemoved { get; set; }

    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
    public void Fail(string message) { Failed = true; ErrorMessage = message; }
}
