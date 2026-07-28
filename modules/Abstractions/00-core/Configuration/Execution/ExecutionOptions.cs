namespace JoinCode.Abstractions.Configuration.Execution;

public sealed class ExecutionOptions
{
    public int SimulatedWorkDurationMs { get; init; } = 5000;
    public int MaxConcurrentTasks { get; init; } = 12;
    public bool VerboseLogging { get; init; } = true;
}
