namespace JoinCode.Abstractions.LLM.Execution;

public sealed class QueryOptions
{
    public IReadOnlyList<string>? AllowedTools { get; init; }
    public IReadOnlyList<string>? DeniedTools { get; init; }
    public ContentReplacementState? ContentReplacementState { get; init; }
    public string? SessionId { get; init; }
    public HashSet<string>? NeverPersistTools { get; init; }
    public Action<IReadOnlyList<ContentReplacementRecord>>? WriteToTranscript { get; init; }
    public CacheSafeParams? CacheSafeParams { get; init; }
    public IProgressTracker? ProgressTracker { get; init; }
    public EffortLevel? EffortLevel { get; init; }
    public string? ModelId { get; init; }

    public bool IsToolAllowed(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (DeniedTools is not null && DeniedTools.Count > 0)
        {
            if (DeniedTools.Contains(toolName))
                return false;
        }

        if (AllowedTools is not null && AllowedTools.Count > 0)
        {
            return AllowedTools.Contains(toolName);
        }

        return true;
    }
}
