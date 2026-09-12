namespace JoinCode.CodeIndex;

/// <summary>
/// 依赖图 — 重写为基于 InMemoryIndexStore 的实时查询
/// store 已维护 DepsBySource/DepsByTarget/DepsByFile 索引,无需额外缓存层
/// </summary>
public sealed class DependencyGraph : IDependencyGraph
{
    private readonly InMemoryIndexStore _store;
    private int _cacheVersion;

    public DependencyGraph(InMemoryIndexStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    internal void InvalidateCache()
    {
        Interlocked.Increment(ref _cacheVersion);
    }

    internal Task InvalidateCacheForFileAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        Interlocked.Increment(ref _cacheVersion);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DependencyEdge>> GetInheritorsAsync(string symbolName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(symbolName);

        using var scope = _store.EnterReadLock();
        if (_store.DepsByTarget.TryGetValue(symbolName, out var list))
        {
            var result = list
                .Where(e => e.DependencyKind is DependencyKind.Inherits or DependencyKind.Implements)
                .ToList();
            return Task.FromResult<IReadOnlyList<DependencyEdge>>(result);
        }
        return Task.FromResult<IReadOnlyList<DependencyEdge>>(Array.Empty<DependencyEdge>());
    }

    public Task<IReadOnlyList<DependencyEdge>> GetDependenciesAsync(string symbolName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(symbolName);

        using var scope = _store.EnterReadLock();
        if (_store.DepsBySource.TryGetValue(symbolName, out var list))
        {
            return Task.FromResult<IReadOnlyList<DependencyEdge>>(list.ToList());
        }
        return Task.FromResult<IReadOnlyList<DependencyEdge>>(Array.Empty<DependencyEdge>());
    }

    public Task<IReadOnlyList<string>> GetAffectedFilesAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var affectedFiles = new HashSet<string>(StringComparer.Ordinal) { filePath };
        var visitedSymbols = new HashSet<string>(StringComparer.Ordinal);

        using (var scope = _store.EnterReadLock())
        {
            // 找出该文件包含的所有符号(基于符号索引)
            var symbolsInFile = _store.SymbolsByFile.TryGetValue(filePath, out var list)
                ? list.Select(s => s.FullyQualifiedName).ToList()
                : new List<string>();

            if (symbolsInFile.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<string>>(affectedFiles.ToList());
            }

            // BFS 反向查找所有依赖这些符号的源符号
            var queue = new Queue<string>();
            foreach (var sym in symbolsInFile)
            {
                if (visitedSymbols.Add(sym)) queue.Enqueue(sym);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!_store.DepsByTarget.TryGetValue(current, out var deps)) continue;

                foreach (var edge in deps)
                {
                    if (visitedSymbols.Add(edge.SourceSymbol))
                    {
                        queue.Enqueue(edge.SourceSymbol);
                    }
                }
            }
        }

        // 通过符号 → 找回文件
        using (var scope = _store.EnterReadLock())
        {
            foreach (var sym in visitedSymbols)
            {
                if (_store.SymbolsByFqn.TryGetValue(sym, out var symbolInfo))
                {
                    affectedFiles.Add(symbolInfo.FilePath);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(affectedFiles.ToList());
    }
}
