namespace JoinCode.Abstractions.LLM.Execution;

public sealed class QueryOptions
{
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public IReadOnlyList<string> DeniedTools { get; init; } = [];
    public ContentReplacementState? ContentReplacementState { get; init; }
    public string? SessionId { get; init; }
    public HashSet<string> NeverPersistTools { get; init; } = [];
    public Action<IReadOnlyList<ContentReplacementRecord>>? WriteToTranscript { get; init; }
    public CacheSafeParams? CacheSafeParams { get; init; }
    public IProgressTracker? ProgressTracker { get; init; }
    public EffortLevel? EffortLevel { get; init; }
    public string? ModelId { get; init; }

    private HashSet<string> _deniedSet = [];
    private HashSet<string> _allowedSet = [];

    public bool IsToolAllowed(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (DeniedTools.Count > 0)
        {
            if (_deniedSet.Count == 0)
                _deniedSet = new HashSet<string>(DeniedTools);
            if (_deniedSet.Contains(toolName))
                return false;
        }

        if (AllowedTools.Count > 0)
        {
            if (_allowedSet.Count == 0)
                _allowedSet = new HashSet<string>(AllowedTools);
            return _allowedSet.Contains(toolName);
        }

        return true;
    }
}
