namespace JoinCode.Abstractions.LLM.Execution;

public sealed class QueryOptions {
    /// <summary>获取允许的工具列表。</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    /// <summary>获取拒绝的工具列表。</summary>
    public IReadOnlyList<string> DeniedTools { get; init; } = [];
    /// <summary>获取内容替换状态。</summary>
    public ContentReplacementState? ContentReplacementState { get; init; }
    /// <summary>获取会话标识。</summary>
    public string? SessionId { get; init; }
    /// <summary>获取不持久化的工具集合。</summary>
    public HashSet<string> NeverPersistTools { get; init; } = [];
    /// <summary>获取写入转录回调。</summary>
    public Action<IReadOnlyList<ContentReplacementRecord>>? WriteToTranscript { get; init; }
    /// <summary>获取缓存安全参数。</summary>
    public CacheSafeParams? CacheSafeParams { get; init; }
    /// <summary>获取进度跟踪器。</summary>
    public IProgressTracker? ProgressTracker { get; init; }
    /// <summary>获取推理努力级别。</summary>
    public EffortLevel? EffortLevel { get; init; }
    /// <summary>获取模型标识。</summary>
    public string? ModelId { get; init; }

    private HashSet<string> _deniedSet = [];
    private HashSet<string> _allowedSet = [];

    /// <summary>判断指定工具是否被允许。</summary>
    public bool IsToolAllowed(string toolName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (DeniedTools.Count > 0) {
            if (_deniedSet.Count == 0)
                _deniedSet = new HashSet<string>(DeniedTools);
            if (_deniedSet.Contains(toolName))
                return false;
        }

        if (AllowedTools.Count > 0) {
            if (_allowedSet.Count == 0)
                _allowedSet = new HashSet<string>(AllowedTools);
            return _allowedSet.Contains(toolName);
        }

        return true;
    }
}