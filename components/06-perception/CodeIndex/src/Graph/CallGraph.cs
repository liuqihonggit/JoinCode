namespace CodeIndex;

/// <summary>
/// 调用图 — 重写为基于 InMemoryIndexStore 的实时查询
/// store 已维护 CallsByCaller/CallsByCallee/CallsByFile 索引,无需额外缓存层
/// 文件增量更新通过 SymbolIndex 直接写入 store,图查询自动反映最新状态
/// </summary>
public sealed class CallGraph : ICallGraph
{
    private readonly InMemoryIndexStore _store;
    private int _cacheVersion;

    public CallGraph(InMemoryIndexStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// 兼容旧 API — InMemoryIndexStore 实时同步,版本号自增即可
    /// </summary>
    internal void InvalidateCache()
    {
        Interlocked.Increment(ref _cacheVersion);
    }

    /// <summary>
    /// 兼容旧 API — 单文件失效,内存版本自动反映,仅记录版本
    /// </summary>
    internal Task InvalidateCacheForFileAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        Interlocked.Increment(ref _cacheVersion);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CallEdge>> GetCallersAsync(string symbolName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(symbolName);

        using var scope = _store.EnterReadLock();
        if (_store.CallsByCallee.TryGetValue(symbolName, out var list))
        {
            return Task.FromResult<IReadOnlyList<CallEdge>>(list.ToList());
        }
        return Task.FromResult<IReadOnlyList<CallEdge>>(Array.Empty<CallEdge>());
    }

    public Task<IReadOnlyList<CallEdge>> GetCalleesAsync(string symbolName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(symbolName);

        using var scope = _store.EnterReadLock();
        if (_store.CallsByCaller.TryGetValue(symbolName, out var list))
        {
            return Task.FromResult<IReadOnlyList<CallEdge>>(list.ToList());
        }
        return Task.FromResult<IReadOnlyList<CallEdge>>(Array.Empty<CallEdge>());
    }

    public Task<IReadOnlyList<CallEdge>> GetCallChainAsync(string from, string to, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        using var scope = _store.EnterReadLock();
        var path = BfsPath(_store.CallsByCaller, from, to);
        return Task.FromResult<IReadOnlyList<CallEdge>>(path);
    }

    public Task<IReadOnlyList<string>> GetImpactScopeAsync(string symbolName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(symbolName);

        using var scope = _store.EnterReadLock();
        var visited = new HashSet<string>(StringComparer.Ordinal) { symbolName };
        var queue = new Queue<string>();
        queue.Enqueue(symbolName);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_store.CallsByCallee.TryGetValue(current, out var callers)) continue;

            foreach (var edge in callers)
            {
                if (visited.Add(edge.CallerSymbol))
                {
                    queue.Enqueue(edge.CallerSymbol);
                }
            }
        }

        visited.Remove(symbolName);
        return Task.FromResult<IReadOnlyList<string>>(visited.ToList());
    }

    private static IReadOnlyList<CallEdge> BfsPath(Dictionary<string, List<CallEdge>> adj, string from, string to)
    {
        if (!adj.TryGetValue(from, out _))
        {
            return Array.Empty<CallEdge>();
        }

        var parentMap = new Dictionary<string, CallEdge>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { from };

        var queue = new Queue<string>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adj.TryGetValue(current, out var neighbors)) continue;

            foreach (var nextEdge in neighbors)
            {
                if (!visited.Add(nextEdge.CalleeSymbol)) continue;

                parentMap[nextEdge.CalleeSymbol] = nextEdge;

                if (nextEdge.CalleeSymbol == to)
                {
                    return ReconstructPath(parentMap, from, to);
                }

                queue.Enqueue(nextEdge.CalleeSymbol);
            }
        }

        return Array.Empty<CallEdge>();
    }

    private static List<CallEdge> ReconstructPath(Dictionary<string, CallEdge> parentMap, string from, string to)
    {
        var path = new List<CallEdge>();
        var current = to;

        while (current != from)
        {
            var edge = parentMap[current];
            path.Add(edge);
            current = edge.CallerSymbol;
        }

        path.Reverse();
        return path;
    }
}
