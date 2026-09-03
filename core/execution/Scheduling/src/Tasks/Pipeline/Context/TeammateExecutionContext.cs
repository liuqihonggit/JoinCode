namespace Core.Scheduling.Tasks;


public sealed class TeammateExecutionContext : IPipelineContext
{
    public required InProcessTeammateDefinition Definition { get; init; }

    public CancellationToken CancellationToken { get; init; }

    public DateTime StartTime { get; init; } = DateTime.UtcNow;

    public IAgent? Agent { get; set; }

    public TeammateState? State { get; set; }

    public CancellationTokenSource? LifecycleCts { get; set; }

    public bool ContinuousModeHandled { get; set; }

    public AgentTaskResult? Result { get; set; }

    public Func<InProcessTeammateDefinition, TeammateState, CancellationToken, Task>? RunLoopAsync { get; set; }

    public Func<string, Task>? TryCleanupAsync { get; set; }

    public Func<string, TeammateState, Task>? CleanupAsync { get; set; }

    public ConcurrentDictionary<string, TeammateState> ActiveTeammates { get; set; } = new();

    public ConcurrentDictionary<string, Channel<CoordinatorMessage>> PendingMessages { get; set; } = new();

    public AsyncLock? TeammateLock { get; set; }

    bool IPipelineContext.Failed { get; set; }
    string? IPipelineContext.ErrorMessage { get; set; }
    void IPipelineContext.Fail(string message)
    {
        ((IPipelineContext)this).Failed = true;
        ((IPipelineContext)this).ErrorMessage = message;
    }
}
