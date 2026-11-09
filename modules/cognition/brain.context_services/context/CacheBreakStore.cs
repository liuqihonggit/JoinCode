namespace Core.Context;

/// <summary>
/// 缓存破坏检测存储实现 — 管理按 agentId 隔离的 CacheBreakDetector
/// <para>单一数据源: ImmutableHamT&lt;string, CacheBreakDetector&gt; _detectorsByAgent</para>
/// <para>GetOrAddDetector 使用 CAS 无锁并发; 其余方法由 Actor Consumer 串行调用。</para>
/// </summary>
internal sealed class CacheBreakStore : ICacheBreakStore {
    private ImmutableHamT<string, CacheBreakDetector> _detectorsByAgent = ImmutableHamT<string, CacheBreakDetector>.Empty;

    private CacheBreakDetector GetOrAddDetector(string? agentId) {
        var key = agentId ?? "main";
        if (_detectorsByAgent.TryGetValue(key, out var existing)) return existing;
        var newDetector = new CacheBreakDetector();
        while (true) {
            var current = _detectorsByAgent;
            if (current.TryGetValue(key, out existing)) return existing;
            var updated = current.Add(key, newDetector);
            if (Interlocked.CompareExchange(ref _detectorsByAgent, updated, current) == current) return newDetector;
        }
    }

    /// <inheritdoc />
    public PromptStateSnapshot RecordPromptState(
        string? agentId,
        ImmutablePrefix prefix,
        string dynamicContent,
        IReadOnlyList<ApiMessage> messages) {
        return GetOrAddDetector(agentId).RecordPromptState(prefix, dynamicContent, messages);
    }

    /// <inheritdoc />
    public CacheBreakResult CheckCacheBreak(
        string? agentId,
        PromptStateSnapshot snapshot,
        ImmutablePrefix currentPrefix,
        string currentDynamicContent,
        TokenUsage usage,
        IReadOnlyList<ApiMessage> messages) {
        return GetOrAddDetector(agentId).CheckCacheBreak(snapshot, currentPrefix, currentDynamicContent, usage, messages);
    }

    /// <inheritdoc />
    public void NotifyCompaction(string? agentId) {
        GetOrAddDetector(agentId).NotifyCompaction();
    }
}
