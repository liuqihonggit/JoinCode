namespace JoinCode.CodeIndex;

/// <summary>
/// 依赖图 — 重写为基于 InMemoryIndexStore 的实时查询
/// store 已维护 DepsBySource/DepsByTarget/DepsByFile 索引,无需额外缓存层
/// </summary>
public sealed class DependencyGraph : IDependencyGraph {
    private readonly InMemoryIndexStore _store;
    private int _cacheVersion;

    /// <summary>
    /// 构造依赖图
    /// </summary>
    /// <param name="store">内存索引存储（维护 DepsBySource/DepsByTarget/DepsByFile 索引）</param>
    public DependencyGraph(InMemoryIndexStore store) {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// 使缓存失效 — 递增版本号强制下次查询重新读取
    /// </summary>
    internal void InvalidateCache() {
        Interlocked.Increment(ref _cacheVersion);
    }

    /// <summary>
    /// 使指定文件的缓存失效
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    internal Task InvalidateCacheForFileAsync(string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);
        Interlocked.Increment(ref _cacheVersion);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取指定符号的所有继承者（Inherits/Implements 依赖）
    /// </summary>
    /// <param name="symbolName">符号完全限定名</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>继承边列表</returns>
    public Task<IReadOnlyList<DependencyEdge>> GetInheritorsAsync(string symbolName, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(symbolName);

        using var scope = _store.EnterReadLock();
        if (_store.DepsByTarget.TryGetValue(symbolName, out var list)) {
            var result = list
                .Where(e => e.DependencyKind is DependencyKind.Inherits or DependencyKind.Implements)
                .ToList();
            return Task.FromResult<IReadOnlyList<DependencyEdge>>(result);
        }
        return Task.FromResult<IReadOnlyList<DependencyEdge>>(Array.Empty<DependencyEdge>());
    }

    /// <summary>
    /// 获取指定符号的所有依赖
    /// </summary>
    /// <param name="symbolName">符号完全限定名</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>依赖边列表</returns>
    public Task<IReadOnlyList<DependencyEdge>> GetDependenciesAsync(string symbolName, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(symbolName);

        using var scope = _store.EnterReadLock();
        if (_store.DepsBySource.TryGetValue(symbolName, out var list)) {
            return Task.FromResult<IReadOnlyList<DependencyEdge>>(list.ToList());
        }
        return Task.FromResult<IReadOnlyList<DependencyEdge>>(Array.Empty<DependencyEdge>());
    }

    /// <summary>
    /// 获取受指定文件变更影响的所有文件 — BFS 反向查找所有依赖这些符号的源符号所在文件
    /// </summary>
    /// <param name="filePath">触发变更的文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>受影响文件路径列表（含输入文件本身）</returns>
    public Task<IReadOnlyList<string>> GetAffectedFilesAsync(string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);

        var affectedFiles = new HashSet<string>(StringComparer.Ordinal) { filePath };
        var visitedSymbols = new HashSet<string>(StringComparer.Ordinal);

        using (var scope = _store.EnterReadLock()) {
            // 找出该文件包含的所有符号(基于符号索引)
            var symbolsInFile = _store.SymbolsByFile.TryGetValue(filePath, out var list)
                ? list.Select(s => s.FullyQualifiedName).ToList()
                : new List<string>();

            if (symbolsInFile.Count == 0) {
                return Task.FromResult<IReadOnlyList<string>>(affectedFiles.ToList());
            }

            // BFS 反向查找所有依赖这些符号的源符号
            var queue = new Queue<string>();
            foreach (var sym in symbolsInFile) {
                if (visitedSymbols.Add(sym)) queue.Enqueue(sym);
            }

            while (queue.Count > 0) {
                var current = queue.Dequeue();
                if (!_store.DepsByTarget.TryGetValue(current, out var deps)) continue;

                foreach (var edge in deps) {
                    if (visitedSymbols.Add(edge.SourceSymbol)) {
                        queue.Enqueue(edge.SourceSymbol);
                    }
                }
            }
        }

        // 通过符号 → 找回文件
        using (var scope = _store.EnterReadLock()) {
            foreach (var sym in visitedSymbols) {
                if (_store.SymbolsByFqn.TryGetValue(sym, out var symbolInfo)) {
                    affectedFiles.Add(symbolInfo.FilePath);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(affectedFiles.ToList());
    }
}