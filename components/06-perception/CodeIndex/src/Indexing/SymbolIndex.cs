namespace JoinCode.CodeIndex;

/// <summary>
/// 符号索引器 — 重写为基于 InMemoryIndexStore 的内存字典操作
/// 不再使用 SQLite 事务,所有写操作在写锁内原子完成
/// FTS5 全文检索替代为字符串包含匹配(后续可集成 SearchService 做 rg 模糊检索)
/// </summary>
public sealed class SymbolIndex : ISymbolIndex, IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private readonly ILanguagePlugin _plugin;
    private readonly IClockService _clock;
    private int _disposed;

    public SymbolIndex(InMemoryIndexStore store, IFileSystem fs, ILanguagePlugin plugin, IClockService? clock = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(plugin);

        _store = store;
        _fs = fs;
        _plugin = plugin;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task IndexFileAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!_fs.FileExists(filePath))
        {
            return;
        }

        var (sourceCode, contentHash) = await HashUtility.ReadFileAndComputeHashAsync(filePath, _fs, ct).ConfigureAwait(false);
        await IndexFileWithContentAsync(filePath, sourceCode, contentHash, ct).ConfigureAwait(false);
    }

    public async Task IndexFileWithContentAsync(string filePath, string sourceCode, string contentHash, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(contentHash);

        var extraction = await _plugin.ExtractAllAsync(sourceCode, filePath, ct).ConfigureAwait(false);
        await IndexFileWithContentAsync(filePath, sourceCode, contentHash, extraction, ct).ConfigureAwait(false);
    }

    public Task IndexFileWithContentAsync(string filePath, string sourceCode, string contentHash, ExtractionResult extraction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(extraction);

        // 单文件原子写: 在写锁内完成 移除旧→插入新→更新追踪
        using var scope = _store.EnterWriteLock();
        RemoveFileInternal(filePath);
        InsertSymbolsInternal(extraction.Symbols);
        InsertCallEdgesInternal(extraction.Calls);
        InsertDependencyEdgesInternal(extraction.Dependencies);
        CorrectInheritsToImplementsInternal();
        UpsertFileTrackingInternal(filePath, contentHash, extraction.Symbols.Count);
        _store.LastUpdated = _clock.GetUtcNowOffset();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 批量索引写入 — 在单个写锁内处理多个已提取的文件,仅在批量结束后执行一次
    /// Inherits→Implements 修正(替代每文件执行 O(n*files) 全量扫描)
    /// 用于全量构建场景;增量更新仍用单文件 <see cref="IndexFileWithContentAsync(string, string, string, System.Threading.CancellationToken)"/>
    /// </summary>
    public Task IndexFilesBatchAsync(
        IReadOnlyList<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)> files,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return Task.CompletedTask;
        }

        // 单写锁批量处理: 消除每文件锁开销 + 每文件 CorrectInheritsToImplements 全量扫描
        using var scope = _store.EnterWriteLock();
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (filePath, _, hash, extraction) = files[i];
            RemoveFileInternal(filePath);
            InsertSymbolsInternal(extraction.Symbols);
            InsertCallEdgesInternal(extraction.Calls);
            InsertDependencyEdgesInternal(extraction.Dependencies);
            UpsertFileTrackingInternal(filePath, hash, extraction.Symbols.Count);
        }

        // 批量结束后只执行一次 Inherits→Implements 修正
        CorrectInheritsToImplementsInternal();
        _store.LastUpdated = _clock.GetUtcNowOffset();
        return Task.CompletedTask;
    }

    public async Task IndexFilesAsync(IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        foreach (var fp in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            await IndexFileAsync(fp, ct).ConfigureAwait(false);
        }
    }

    public Task RemoveFileAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var scope = _store.EnterWriteLock();
        RemoveFileInternal(filePath);
        _store.FileTracking.Remove(filePath);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct)
    {
        _store.Clear();
        return Task.CompletedTask;
    }

    public Task<IndexStats> GetStatsAsync(CancellationToken ct)
    {
        using var scope = _store.EnterReadLock();
        var stats = new IndexStats
        {
            FileCount = _store.FileTracking.Count,
            SymbolCount = _store.SymbolsByFqn.Count,
            CallEdgeCount = _store.CallEdges.Count,
            DependencyEdgeCount = _store.DepEdges.Count,
            ProjectCount = _store.Projects.Count,
            LastUpdated = _store.LastUpdated
        };
        return Task.FromResult(stats);
    }

    /// <summary>
    /// 移除文件相关所有数据(必须在写锁内调用)— 替代 DELETE FROM 语句
    /// </summary>
    private void RemoveFileInternal(string filePath)
    {
        // 移除该文件的所有符号
        if (_store.SymbolsByFile.TryGetValue(filePath, out var symbolsInFile))
        {
            foreach (var sym in symbolsInFile)
            {
                _store.SymbolsByFqn.Remove(sym.FullyQualifiedName);

                if (_store.SymbolsByName.TryGetValue(sym.Name, out var nameList))
                {
                    nameList.Remove(sym);
                    if (nameList.Count == 0) _store.SymbolsByName.Remove(sym.Name);
                }

                if (_store.SymbolsByKind.TryGetValue(sym.Kind, out var kindList))
                {
                    kindList.Remove(sym);
                    if (kindList.Count == 0) _store.SymbolsByKind.Remove(sym.Kind);
                }
            }
            _store.SymbolsByFile.Remove(filePath);
        }

        // 移除该文件的所有调用边
        if (_store.CallsByFile.TryGetValue(filePath, out var callsInFile))
        {
            foreach (var edge in callsInFile)
            {
                _store.CallEdges.Remove(edge);
                if (_store.CallsByCaller.TryGetValue(edge.CallerSymbol, out var callerList))
                {
                    callerList.Remove(edge);
                    if (callerList.Count == 0) _store.CallsByCaller.Remove(edge.CallerSymbol);
                }
                if (_store.CallsByCallee.TryGetValue(edge.CalleeSymbol, out var calleeList))
                {
                    calleeList.Remove(edge);
                    if (calleeList.Count == 0) _store.CallsByCallee.Remove(edge.CalleeSymbol);
                }
            }
            _store.CallsByFile.Remove(filePath);
        }

        // 移除该文件的所有依赖边
        if (_store.DepsByFile.TryGetValue(filePath, out var depsInFile))
        {
            foreach (var edge in depsInFile)
            {
                _store.DepEdges.Remove(edge);
                if (_store.DepsBySource.TryGetValue(edge.SourceSymbol, out var srcList))
                {
                    srcList.Remove(edge);
                    if (srcList.Count == 0) _store.DepsBySource.Remove(edge.SourceSymbol);
                }
                if (_store.DepsByTarget.TryGetValue(edge.TargetSymbol, out var tgtList))
                {
                    tgtList.Remove(edge);
                    if (tgtList.Count == 0) _store.DepsByTarget.Remove(edge.TargetSymbol);
                }
            }
            _store.DepsByFile.Remove(filePath);
        }
    }

    private void InsertSymbolsInternal(IReadOnlyList<SymbolInfo> symbols)
    {
        foreach (var symbol in symbols)
        {
            // FQN 相同则覆盖(等价 ON CONFLICT)
            if (_store.SymbolsByFqn.TryGetValue(symbol.FullyQualifiedName, out var existing))
            {
                // 从所有索引中移除旧符号
                if (_store.SymbolsByName.TryGetValue(existing.Name, out var nameList))
                {
                    nameList.Remove(existing);
                    if (nameList.Count == 0) _store.SymbolsByName.Remove(existing.Name);
                }
                if (_store.SymbolsByFile.TryGetValue(existing.FilePath, out var fileList))
                {
                    fileList.Remove(existing);
                    if (fileList.Count == 0) _store.SymbolsByFile.Remove(existing.FilePath);
                }
                if (_store.SymbolsByKind.TryGetValue(existing.Kind, out var kindList))
                {
                    kindList.Remove(existing);
                    if (kindList.Count == 0) _store.SymbolsByKind.Remove(existing.Kind);
                }
            }

            _store.SymbolsByFqn[symbol.FullyQualifiedName] = symbol;

            if (!_store.SymbolsByName.TryGetValue(symbol.Name, out var byName))
            {
                byName = new List<SymbolInfo>();
                _store.SymbolsByName[symbol.Name] = byName;
            }
            byName.Add(symbol);

            if (!_store.SymbolsByFile.TryGetValue(symbol.FilePath, out var byFile))
            {
                byFile = new List<SymbolInfo>();
                _store.SymbolsByFile[symbol.FilePath] = byFile;
            }
            byFile.Add(symbol);

            if (!_store.SymbolsByKind.TryGetValue(symbol.Kind, out var byKind))
            {
                byKind = new List<SymbolInfo>();
                _store.SymbolsByKind[symbol.Kind] = byKind;
            }
            byKind.Add(symbol);
        }
    }

    private void InsertCallEdgesInternal(IReadOnlyList<CallEdge> calls)
    {
        foreach (var call in calls)
        {
            _store.CallEdges.Add(call);

            if (!_store.CallsByCaller.TryGetValue(call.CallerSymbol, out var callerList))
            {
                callerList = new List<CallEdge>();
                _store.CallsByCaller[call.CallerSymbol] = callerList;
            }
            callerList.Add(call);

            if (!_store.CallsByCallee.TryGetValue(call.CalleeSymbol, out var calleeList))
            {
                calleeList = new List<CallEdge>();
                _store.CallsByCallee[call.CalleeSymbol] = calleeList;
            }
            calleeList.Add(call);

            if (!_store.CallsByFile.TryGetValue(call.CallSiteFilePath, out var fileList))
            {
                fileList = new List<CallEdge>();
                _store.CallsByFile[call.CallSiteFilePath] = fileList;
            }
            fileList.Add(call);
        }
    }

    private void InsertDependencyEdgesInternal(IReadOnlyList<DependencyEdge> deps)
    {
        foreach (var dep in deps)
        {
            _store.DepEdges.Add(dep);

            if (!_store.DepsBySource.TryGetValue(dep.SourceSymbol, out var srcList))
            {
                srcList = new List<DependencyEdge>();
                _store.DepsBySource[dep.SourceSymbol] = srcList;
            }
            srcList.Add(dep);

            if (!_store.DepsByTarget.TryGetValue(dep.TargetSymbol, out var tgtList))
            {
                tgtList = new List<DependencyEdge>();
                _store.DepsByTarget[dep.TargetSymbol] = tgtList;
            }
            tgtList.Add(dep);

            if (!string.IsNullOrEmpty(dep.SourceFilePath))
            {
                if (!_store.DepsByFile.TryGetValue(dep.SourceFilePath!, out var fileList))
                {
                    fileList = new List<DependencyEdge>();
                    _store.DepsByFile[dep.SourceFilePath!] = fileList;
                }
                fileList.Add(dep);
            }
        }
    }

    /// <summary>
    /// 修正 Inherits→Implements: 当 target 是接口时,Inherits 边替换为 Implements
    /// record DTO 不可变,需创建新边替换旧边(在所有索引中替换)
    /// </summary>
    private void CorrectInheritsToImplementsInternal()
    {
        // 先收集需要修正的边: old -> new 映射,避免循环内 IndexOf 导致 O(n²)
        var replacements = new Dictionary<DependencyEdge, DependencyEdge>();
        for (var i = 0; i < _store.DepEdges.Count; i++)
        {
            var dep = _store.DepEdges[i];
            if (dep.DependencyKind != DependencyKind.Inherits) continue;
            if (!_store.SymbolsByFqn.TryGetValue(dep.TargetSymbol, out var target) || target.Kind != SymbolKind.Interface) continue;

            var newDep = new DependencyEdge
            {
                SourceSymbol = dep.SourceSymbol,
                TargetSymbol = dep.TargetSymbol,
                DependencyKind = DependencyKind.Implements,
                SourceFilePath = dep.SourceFilePath
            };
            _store.DepEdges[i] = newDep;
            replacements[dep] = newDep;
        }

        if (replacements.Count == 0) return;

        // 单次遍历替换三个二级索引中的旧边
        ReplaceEdgesInLists(_store.DepsBySource, replacements);
        ReplaceEdgesInLists(_store.DepsByTarget, replacements);
        ReplaceEdgesInLists(_store.DepsByFile, replacements);
    }

    private static void ReplaceEdgesInLists<TKey>(Dictionary<TKey, List<DependencyEdge>> dict, Dictionary<DependencyEdge, DependencyEdge> replacements) where TKey : notnull
    {
        foreach (var kv in dict)
        {
            var list = kv.Value;
            for (var i = 0; i < list.Count; i++)
            {
                if (replacements.TryGetValue(list[i], out var newDep))
                {
                    list[i] = newDep;
                }
            }
        }
    }

    private void UpsertFileTrackingInternal(string filePath, string hash, int symbolCount)
    {
        var now = _clock.GetUtcNowOffset();
        if (_store.FileTracking.TryGetValue(filePath, out var entry))
        {
            entry.Hash = hash;
            entry.SymbolCount = symbolCount;
            entry.LastModified = now;
        }
        else
        {
            _store.FileTracking[filePath] = new FileTrackingEntry
            {
                FilePath = filePath,
                Hash = hash,
                SymbolCount = symbolCount,
                LastModified = now
            };
        }
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed))
        {
            return;
        }
    }
}
