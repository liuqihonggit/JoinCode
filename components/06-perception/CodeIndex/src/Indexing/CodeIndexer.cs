namespace JoinCode.CodeIndex;

[Register]
public sealed partial class CodeIndexer : ICodeIndexer, IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private readonly SymbolIndex _symbolIndex;
    private readonly IncrementalUpdater _updater;
    private readonly SymbolSearcher _searcher;
    private readonly CallGraph _callGraph;
    private readonly DependencyGraph _dependencyGraph;
    private readonly ProjectDependencyGraph _projectDependencyGraph;
    private readonly ProjectIndex _projectIndex;
    private readonly CSharpSymbolExtractor _plugin;
    private int _disposed;

    public CodeIndexer(InMemoryIndexStore store, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);

        _store = store;
        _fs = fs;
        _plugin = new CSharpSymbolExtractor();
        _symbolIndex = new SymbolIndex(store, fs, _plugin);
        _updater = new IncrementalUpdater(_symbolIndex, store, fs, () => new CSharpSymbolExtractor());
        _searcher = new SymbolSearcher(store);
        _callGraph = new CallGraph(store);
        _dependencyGraph = new DependencyGraph(store);
        _projectDependencyGraph = new ProjectDependencyGraph(store);
        _projectIndex = new ProjectIndex(store, fs);
    }

    public ISymbolSearcher Searcher => _searcher;
    public ICallGraph CallGraph => _callGraph;
    public IDependencyGraph DependencyGraph => _dependencyGraph;
    public IProjectDependencyGraph ProjectDependencyGraph => _projectDependencyGraph;

    public async Task<BuildIndexResult> BuildIndexAsync(CodeIndexOptions options, CancellationToken ct, IProgress<IndexProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var totalSw = Stopwatch.StartNew();

        // Phase A: 索引项目依赖(.slnx/.sln/.csproj)
        await IndexProjectsAsync(options.WorkspaceRoot, ct).ConfigureAwait(false);

        // Phase B: 扫描 .cs 文件(跳过 bin/obj)
        var csFiles = CollectCsFiles(options.WorkspaceRoot, options.ExcludePatterns);
        var trackedFiles = GetTrackedFilesInWorkspace(options.WorkspaceRoot);

        // Phase C: 并行读文件+哈希(Task.WhenAll,对齐 IncrementalUpdater 模式)
        var storedHashes = BatchGetStoredHashes(csFiles);

        var updatedCount = 0;
        var skippedCount = 0;
        var deletedCount = 0;
        var total = csFiles.Count;

        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesToIndex = new List<(string FilePath, string SourceCode, string Hash)>();

        // 并行 IO: 一次性启动所有读+哈希任务,Task.WhenAll 等待全部完成
        // (IncrementalUpdater.UpdateDirectoryAsync 已验证此模式,OS 处理 IO 并发)
        var readTasks = csFiles.Select(async filePath =>
        {
            ct.ThrowIfCancellationRequested();
            var (sourceCode, currentHash) = await HashUtility.ReadFileAndComputeHashAsync(filePath, _fs, ct).ConfigureAwait(false);
            return (FilePath: filePath, SourceCode: sourceCode, Hash: currentHash);
        }).ToArray();

        var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);

        foreach (var r in readResults)
        {
            if (storedHashes.TryGetValue(r.FilePath, out var storedHash) && storedHash == r.Hash)
            {
                skippedCount++;
            }
            else
            {
                filesToIndex.Add((r.FilePath, r.SourceCode, r.Hash));
            }
            existingFiles.Add(r.FilePath);
        }

        progress?.Report(new IndexProgress { Current = total, Total = total });

        // Phase D: 并行提取符号(已并行,4度)
        var extractionResults = ParallelExtractAll(filesToIndex, ct);

        // Phase E: 批量索引写入(单写锁 + 单次 CorrectInheritsToImplements,替代每文件锁+每文件全量扫描)
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>(filesToIndex.Count);
        for (var i = 0; i < filesToIndex.Count; i++)
        {
            var (filePath, sourceCode, hash) = filesToIndex[i];
            batch.Add((filePath, sourceCode, hash, extractionResults[i]));
        }
        await _symbolIndex.IndexFilesBatchAsync(batch, ct).ConfigureAwait(false);
        updatedCount = batch.Count;

        // Phase F: 删除已移除文件
        foreach (var trackedFile in trackedFiles)
        {
            if (!existingFiles.Contains(trackedFile))
            {
                await _symbolIndex.RemoveFileAsync(trackedFile, ct).ConfigureAwait(false);
                deletedCount++;
            }
        }

        // Phase G: 失效图缓存
        InvalidateGraphCaches();

        totalSw.Stop();

        return new BuildIndexResult
        {
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            DeletedCount = deletedCount
        };
    }

    private async Task IndexProjectsAsync(string workspaceRoot, CancellationToken ct)
    {
        var solutionFiles = CollectFiles(workspaceRoot, "*.slnx")
            .Concat(CollectFiles(workspaceRoot, "*.sln"))
            .ToList();

        foreach (var slnFile in solutionFiles)
        {
            ct.ThrowIfCancellationRequested();
            await _projectIndex.IndexSolutionAsync(slnFile, ct).ConfigureAwait(false);
        }

        if (solutionFiles.Count == 0)
        {
            var csprojFiles = CollectFiles(workspaceRoot, "*.csproj");
            foreach (var csprojFile in csprojFiles)
            {
                ct.ThrowIfCancellationRequested();
                await _projectIndex.IndexProjectAsync(csprojFile, workspaceRoot, ct).ConfigureAwait(false);
            }
        }

        _projectDependencyGraph.InvalidateCache();
    }

    private List<ExtractionResult> ParallelExtractAll(
        List<(string FilePath, string SourceCode, string Hash)> files, CancellationToken ct)
    {
        if (files.Count == 0) return [];

        // 优化: Partitioner.Create 动态范围分区 + PLINQ,替代固定 chunk
        // 优势: 1) PLINQ 动态工作分区,大文件不阻塞小文件(固定 16-chunk 会因文件大小不均导致线程空闲)
        //      2) 每个范围复用 parser+extractor(rangeSize=64,平衡创建开销与负载均衡)
        //      3) WithDegreeOfParallelism = ProcessorCount 充分利用多核
        var parallelism = Math.Min(files.Count, CpuParallelism.GetDegree());
        var results = new ExtractionResult[files.Count];

        // 范围大小 64: 3313 files → 52 ranges,16 线程平均每线程 ~3-4 ranges
        // 大文件只阻塞其所在 64-文件范围,不影响其他范围(原 207-文件 chunk 会阻塞整个线程)
        // 52 个 parser 创建 vs 原 16 个,多 36 次创建但负载均衡收益更大
        var partitioner = Partitioner.Create(0, files.Count, 64);

        partitioner
            .AsParallel()
            .WithDegreeOfParallelism(parallelism)
            .WithCancellation(ct)
            .ForAll(range =>
            {
                using var parser = TreeSitterParserPool.CreateDisposable();
                using var extractor = new CSharpSymbolExtractor(parser);

                for (var i = range.Item1; i < range.Item2; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var f = files[i];
                    results[i] = extractor.ExtractAll(f.SourceCode, f.FilePath);
                }
            });

        return [.. results];
    }

    public async Task UpdateFileAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        await _updater.UpdateAsync(filePath, ct).ConfigureAwait(false);
        await InvalidateGraphCachesForFileAsync(filePath, ct).ConfigureAwait(false);
    }

    public async Task RemoveFileAsync(string filePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        await _symbolIndex.RemoveFileAsync(filePath, ct).ConfigureAwait(false);
        InvalidateGraphCaches();
    }

    private void InvalidateGraphCaches()
    {
        _callGraph.InvalidateCache();
        _dependencyGraph.InvalidateCache();
        _projectDependencyGraph.InvalidateCache();
    }

    private async Task InvalidateGraphCachesForFileAsync(string filePath, CancellationToken ct)
    {
        await _callGraph.InvalidateCacheForFileAsync(filePath, ct).ConfigureAwait(false);
        await _dependencyGraph.InvalidateCacheForFileAsync(filePath, ct).ConfigureAwait(false);
    }

    public async Task<IndexStats> GetStatsAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return await _symbolIndex.GetStatsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 综合检索: rg式模糊匹配符号 → 获取全部函数引用 + 调用方/被调用方,受 token 预算限制
    /// 流程: 模糊匹配 → 收集 references/callers/callees → 按 token 预算截断(优先级: matched > refs > callers > callees)
    /// </summary>
    public async Task<ComprehensiveSearchResult> SearchComprehensiveAsync(string pattern, int maxTokenBudget, CancellationToken ct, bool includeAst = true)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var sw = Stopwatch.StartNew();

        // Step 1: rg 式模糊匹配符号(限制候选上限,避免无限匹配)
        var searchResult = await _searcher.SearchByPatternAsync(pattern, 100, ct).ConfigureAwait(false);
        var allMatched = searchResult.Items;
        var totalMatchedCount = searchResult.TotalCount;  // 真实匹配数(可能 > Items.Count,超过部分被候选上限截断)

        // Step 2: 收集每个匹配符号的引用 + 调用方/被调用方 (includeAst=false 时跳过)
        var allReferences = new List<SymbolInfo>();
        var allCallers = new List<CallEdge>();
        var allCallees = new List<CallEdge>();

        if (includeAst)
        {
            foreach (var symbol in allMatched)
            {
                if (ct.IsCancellationRequested) break;

                var refs = await _searcher.FindReferencesAsync(symbol.Name, ct).ConfigureAwait(false);
                allReferences.AddRange(refs);

                var cs = await _callGraph.GetCallersAsync(symbol.Name, ct).ConfigureAwait(false);
                allCallers.AddRange(cs);

                var cl = await _callGraph.GetCalleesAsync(symbol.Name, ct).ConfigureAwait(false);
                allCallees.AddRange(cl);
            }
        }

        // Step 3: 按 token 预算截断(优先级: matched symbols > references > callers > callees)
        var matchedSymbols = new List<SymbolInfo>();
        var references = new List<SymbolInfo>();
        var callers = new List<CallEdge>();
        var callees = new List<CallEdge>();
        var estimatedTokens = 0;
        var truncated = false;

        // 先物化去重列表(用于截断计数)
        var distinctReferences = allReferences.Distinct().ToList();
        var distinctCallers = allCallers.Distinct().ToList();
        var distinctCallees = allCallees.Distinct().ToList();

        // 填充 matched symbols
        foreach (var s in allMatched)
        {
            var t = EstimateSymbolTokens(s);
            if (estimatedTokens + t > maxTokenBudget)
            {
                truncated = true;
                break;
            }
            matchedSymbols.Add(s);
            estimatedTokens += t;
        }

        // 填充 references (去重)
        foreach (var r in distinctReferences)
        {
            var t = EstimateSymbolTokens(r);
            if (estimatedTokens + t > maxTokenBudget)
            {
                truncated = true;
                break;
            }
            references.Add(r);
            estimatedTokens += t;
        }

        // 填充 callers (去重)
        foreach (var c in distinctCallers)
        {
            var t = EstimateEdgeTokens(c);
            if (estimatedTokens + t > maxTokenBudget)
            {
                truncated = true;
                break;
            }
            callers.Add(c);
            estimatedTokens += t;
        }

        // 填充 callees (去重)
        foreach (var c in distinctCallees)
        {
            var t = EstimateEdgeTokens(c);
            if (estimatedTokens + t > maxTokenBudget)
            {
                truncated = true;
                break;
            }
            callees.Add(c);
            estimatedTokens += t;
        }

        // 计算被截断的条目数(所有类别的截断总和)
        var truncatedCount = (allMatched.Count - matchedSymbols.Count)
                           + (distinctReferences.Count - references.Count)
                           + (distinctCallers.Count - callers.Count)
                           + (distinctCallees.Count - callees.Count);

        sw.Stop();

        return new ComprehensiveSearchResult
        {
            MatchedSymbols = matchedSymbols,
            TotalMatchedCount = totalMatchedCount,
            References = references,
            Callers = callers,
            Callees = callees,
            EstimatedTokens = estimatedTokens,
            Truncated = truncated,
            TruncatedCount = truncatedCount,
            ElapsedMs = sw.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// 估算符号的 token 数 — 约 4 字符/token,符号含 Name+FQN+FilePath 等
    /// </summary>
    private static int EstimateSymbolTokens(SymbolInfo symbol)
    {
        // 简化估算: Name + FQN + FilePath 字符数 / 4, 最低 5 tokens
        var chars = symbol.Name.Length + symbol.FullyQualifiedName.Length + symbol.FilePath.Length;
        return Math.Max(5, chars / 4);
    }

    /// <summary>
    /// 估算调用边的 token 数 — Caller + Callee + FilePath 等
    /// </summary>
    private static int EstimateEdgeTokens(CallEdge edge)
    {
        var chars = edge.CallerSymbol.Length + edge.CalleeSymbol.Length + edge.CallSiteFilePath.Length;
        return Math.Max(4, chars / 4);
    }

    private IReadOnlyList<string> CollectCsFiles(string workspaceRoot, IReadOnlyList<string>? excludePatterns)
    {
        // 默认排除 bin/obj/.git/.x — 避免扫描编译产物和临时目录
        var excludes = (excludePatterns ?? [])
            .Select(p => p.TrimEnd('/', '\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 强制加入 bin/obj/.git/.x (用户明确要求跳过 bin/obj)
        foreach (var forced in new[] { "bin", "obj", ".git", ".x" })
        {
            excludes.Add(forced);
        }

        var result = new List<string>();
        CollectCsFilesRecursive(workspaceRoot, workspaceRoot, excludes, result);
        return result;
    }

    private void CollectCsFilesRecursive(string currentDir, string workspaceRoot, HashSet<string> excludes, List<string> result)
    {
        try
        {
            foreach (var dir in _fs.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly))
            {
                var span = dir.AsSpan();
                var lastSep = span.LastIndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dirName = lastSep >= 0 ? span[(lastSep + 1)..] : span;

                if (excludes.Contains(dirName.ToString()))
                {
                    continue;
                }

                CollectCsFilesRecursive(dir, workspaceRoot, excludes, result);
            }

            foreach (var file in _fs.EnumerateFiles(currentDir, "*.cs", SearchOption.TopDirectoryOnly))
            {
                result.Add(file);
            }
        }
        catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.WriteLine($"CodeIndexer: Access denied scanning directory: {ex.Message}"); }
    }

    private List<string> CollectFiles(string workspaceRoot, string pattern)
    {
        var result = new List<string>();
        try
        {
            foreach (var file in _fs.EnumerateFiles(workspaceRoot, pattern, SearchOption.TopDirectoryOnly))
            {
                result.Add(file);
            }
        }
        catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.WriteLine($"CodeIndexer: Access denied collecting files with pattern {pattern}: {ex.Message}"); }
        return result;
    }

    private Dictionary<string, string> BatchGetStoredHashes(IReadOnlyList<string> filePaths)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (filePaths.Count == 0) return result;

        using var scope = _store.EnterReadLock();
        foreach (var fp in filePaths)
        {
            if (_store.FileTracking.TryGetValue(fp, out var entry))
            {
                result[fp] = entry.Hash;
            }
        }
        return result;
    }

    private IReadOnlyList<string> GetTrackedFilesInWorkspace(string workspaceRoot)
    {
        using var scope = _store.EnterReadLock();
        return _store.FileTracking.Keys
            .Where(p => p.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _updater.Dispose();
        _symbolIndex.Dispose();
    }
}
