namespace JoinCode.CodeIndex;

/// <summary>
/// 代码索引器 — 统一管理符号索引、调用图、依赖图、项目索引和增量更新
/// </summary>
[Register(typeof(ICodeIndexer), ServiceLifetime.Singleton)]
public sealed partial class CodeIndexer : ServiceEntity, ICodeIndexer, IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private SymbolIndex _symbolIndex;
    private IncrementalUpdater _updater;
    private readonly SymbolSearcher _searcher;
    private readonly CallGraph _callGraph;
    private readonly DependencyGraph _dependencyGraph;
    private readonly ProjectDependencyGraph _projectDependencyGraph;
    private readonly ProjectIndex _projectIndex;
    private ILanguagePlugin _plugin;
    private Func<ILanguagePlugin> _pluginFactory;
    private readonly GraphAnalytics _analytics;
    private readonly IIndexStore _persistence;
    private readonly GraphVisualization _visualization;
    private readonly ILogger<CodeIndexer>? _logger;
    private readonly IHttpClientProvider? _httpClient;
    private readonly IKvStore _kvStore;
    private EmbeddingIndex? _embeddingIndex;
    private List<IIndexStore> _indexStores = [];
    private string? _lastVectorIndexDir;
    private int _disposed;
    private int _autoLoadState;
    private string? _autoDiscoveredWorkspaceRoot;
    private static readonly string AutoLoadSubDir = Path.Combine(AppDataConstants.AppDataFolder, "code-index");

    /// <summary>
    /// 构造代码索引器
    /// </summary>
    /// <param name="store">内存索引存储</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="httpClient">可选 HTTP 客户端（用于模型缺失时自动下载，> ADR: 0124）</param>
    /// <param name="kvStore">可选 KV 存储抽象（用于持久化，默认 InMemoryKvStore）</param>
    public CodeIndexer(InMemoryIndexStore store, IFileSystem fs, ILogger<CodeIndexer>? logger = null, IHttpClientProvider? httpClient = null, IKvStore? kvStore = null) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);

        _store = store;
        _fs = fs;
        _logger = logger;
        _httpClient = httpClient;
        _pluginFactory = static () => new CSharpSymbolExtractor();
        _plugin = _pluginFactory();
        _symbolIndex = new SymbolIndex(store, fs, _plugin);
        _updater = new IncrementalUpdater(_symbolIndex, store, fs, _pluginFactory);
        _searcher = new SymbolSearcher(store);
        _callGraph = new CallGraph(store);
        _dependencyGraph = new DependencyGraph(store);
        _projectDependencyGraph = new ProjectDependencyGraph(store);
        _projectIndex = new ProjectIndex(store, fs, logger);
        _analytics = new GraphAnalytics(store);
        var kv = kvStore ?? new InMemoryKvStore();
        _kvStore = kv;
        _persistence = new GraphPersistence(store, kv);
        _visualization = new GraphVisualization(store);
        TryInitEmbeddingIndex(fs, kv, logger);
        BuildIndexStoreList();
    }

    /// <summary>
    /// 构建 _indexStores 列表 — 统一管理所有 IIndexStore 实现，供 LINQ 链式加载/保存。
    /// </summary>
    private void BuildIndexStoreList() {
        _indexStores = [_persistence];
        if (_embeddingIndex is not null) _indexStores.Add(_embeddingIndex);
    }

    /// <summary>
    /// 自动初始化向量索引 — 模型文件存在时创建 OnnxEmbeddingClient + EmbeddingIndex。
    /// <para>模型路径：%AppData%/jcc/embedding/model_quantized.onnx + vocab.txt</para>
    /// <para>文件不存在时静默跳过（向量搜索降级为不可用）。</para>
    /// </summary>
    private void TryInitEmbeddingIndex(IFileSystem fs, IKvStore kvStore, ILogger<CodeIndexer>? logger) {
        var appData = EmbeddingModelDownloader.DefaultTargetDir;
        var modelPath = Path.Combine(appData, EmbeddingModelDownloader.ModelFileName);
        var vocabPath = Path.Combine(appData, EmbeddingModelDownloader.VocabFileName);
        if (!fs.FileExists(modelPath) || !fs.FileExists(vocabPath)) return;

        try {
            var degree = int.TryParse(Environment.GetEnvironmentVariable("JCC_ONNX_DEGREE"), out var d) && d > 0
                ? Math.Min(d, Environment.ProcessorCount) : 0;
            var embedModel = new OnnxEmbeddingClient(modelPath, vocabPath, fs, degree: degree);
            var annType = Environment.GetEnvironmentVariable("JCC_ANN_TYPE") ?? "brute";
            IAnnSearch ann = annType.Equals("hnsw", StringComparison.OrdinalIgnoreCase)
                ? new HnswAnn()
                : new BruteForceAnn();
            _embeddingIndex = new EmbeddingIndex(embedModel, ann, fs);
            logger?.LogInformation("向量索引已自动初始化: dim={Dim}, ann={Ann}", embedModel.Dimensions, annType);
        } catch (Exception ex) {
            logger?.LogWarning(ex, "向量索引自动初始化失败，语义搜索将不可用");
        }
    }

    /// <summary>
    /// 确保向量模型存在 — 缺失且有 HttpClient 时自动下载（对齐 git submodule update --init，> ADR: 0124）。
    /// <para>下载后重新初始化 EmbeddingIndex + BuildIndexStoreList。</para>
    /// <para>已初始化/无 HttpClient/下载失败时静默降级（保持原行为）。</para>
    /// </summary>
    public async Task EnsureEmbeddingModelAsync(CancellationToken ct = default) {
        if (_embeddingIndex is not null) return;
        if (_httpClient is null) return;
        try {
            var downloader = new EmbeddingModelDownloader(_logger as ILogger<EmbeddingModelDownloader>);
            await using var rangeDownloader = new RangeDownloader(_httpClient!, _fs);
            await downloader.EnsureAsync(EmbeddingModelDownloader.DefaultTargetDir, _fs, _httpClient, rangeDownloader, ct).ConfigureAwait(false);
            TryInitEmbeddingIndex(_fs, _kvStore, _logger);
            BuildIndexStoreList();
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "向量模型自动下载失败，语义搜索将不可用");
        }
    }

    /// <summary>
    /// 设置语言插件工厂 — 插件加载时调用(ADR 0098 万物皆插件)
    /// <para>必须在索引加载前调用,否则会丢失已索引数据</para>
    /// </summary>
    public void SetLanguagePluginFactory(Func<ILanguagePlugin> pluginFactory) {
        ArgumentNullException.ThrowIfNull(pluginFactory);
        _pluginFactory = pluginFactory;
        _plugin = _pluginFactory();
        _symbolIndex = new SymbolIndex(_store, _fs, _plugin);
        _updater = new IncrementalUpdater(_symbolIndex, _store, _fs, _pluginFactory);
    }

    /// <summary>
    /// 设置向量嵌入索引 — 启用语义搜索功能。
    /// <para>必须在 BuildIndexAsync 前调用，否则向量索引不会填充。</para>
    /// </summary>
    /// <param name="embeddingIndex">向量嵌入索引实例。</param>
    public void SetEmbeddingIndex(EmbeddingIndex embeddingIndex) {
        ArgumentNullException.ThrowIfNull(embeddingIndex);
        _embeddingIndex = embeddingIndex;
    }

    /// <summary>
    /// 语义搜索 — 通过向量嵌入查找相似代码块。
    /// <para>未设置 EmbeddingIndex 时返回空列表。</para>
    /// <para>options.IncludeSourceText=true 时结果携带块原文；IncludeParentDocument=true 时从文件系统读取父文档原文。</para>
    /// </summary>
    /// <param name="query">查询文本。</param>
    /// <param name="topK">返回结果数上限。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="options">搜索选项 — AI 动态控制召回策略（null 用默认：仅元数据，无原文）。</param>
    /// <returns>匹配的代码块列表，按相似度降序排列。</returns>
    public async Task<IReadOnlyList<ChunkSearchResult>> SearchSemanticAsync(
        string query, int topK, CancellationToken ct, SearchOptions? options = null) {
        if (_embeddingIndex is null) return [];
        return await _embeddingIndex.SearchAsync(query, topK, ct, options).ConfigureAwait(false);
    }

    /// <summary>符号搜索器 — 支持模糊匹配和引用查找</summary>
    public ISymbolSearcher Searcher => _searcher;

    /// <summary>调用图 — 查询函数调用关系</summary>
    public ICallGraph CallGraph => _callGraph;

    /// <summary>依赖图 — 查询符号间依赖关系</summary>
    public IDependencyGraph DependencyGraph => _dependencyGraph;

    /// <summary>项目依赖图 — 查询项目间依赖关系</summary>
    public IProjectDependencyGraph ProjectDependencyGraph => _projectDependencyGraph;

    /// <summary>图分析器 — 提供图算法和统计</summary>
    public IGraphAnalytics Analytics => _analytics;

    /// <summary>图持久化 — 索引的加载和保存</summary>
    public IIndexStore Persistence => _persistence;

    /// <summary>图可视化 — 生成图的可视化输出</summary>
    public IGraphVisualization Visualization => _visualization;

    /// <summary>
    /// 构建工作区索引 — 索引项目依赖 → 扫描 .cs 文件 → 并行读+哈希 → 并行提取符号 → 批量写入 → 删除已移除文件
    /// </summary>
    /// <param name="options">索引选项，含工作区根和排除模式</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="progress">可选进度报告器</param>
    /// <returns>构建结果，含更新/跳过/删除计数</returns>
    public async Task<BuildIndexResult> BuildIndexAsync(CodeIndexOptions options, CancellationToken ct, IProgress<IndexProgress>? progress = null) {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var totalSw = Stopwatch.StartNew();
        var phaseSw = Stopwatch.StartNew();

        // Phase A: 索引项目依赖(.slnx/.sln/.csproj)
        await IndexProjectsAsync(options.WorkspaceRoot, ct).ConfigureAwait(false);

        // Phase B: 扫描 .cs 文件(跳过 bin/obj)
        var csFiles = CollectCsFiles(options.WorkspaceRoot, options.ExcludePatterns);
        var trackedFiles = GetTrackedFilesInWorkspace(options.WorkspaceRoot);
        Console.Error.WriteLine($"[code-index] 扫描: {csFiles.Count} 文件 ({phaseSw.ElapsedMilliseconds}ms)");
        phaseSw.Restart();

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
        // 大文件(>1MB)用 mmap 零拷贝读取，小文件用 ReadAllTextAsync
        const long MmapThreshold = 1024 * 1024;
        var readTasks = csFiles.Select(async filePath => {
            ct.ThrowIfCancellationRequested();
            if (_fs.GetFileLength(filePath) > MmapThreshold) {
                var mapped = await HashUtility.ReadFileAndComputeHashMappedAsync(filePath, ct).ConfigureAwait(false);
                return (FilePath: filePath, SourceCode: mapped.Content, Hash: mapped.Hash);
            }
            var (sourceCode, currentHash) = await HashUtility.ReadFileAndComputeHashAsync(filePath, _fs, ct).ConfigureAwait(false);
            return (FilePath: filePath, SourceCode: sourceCode, Hash: currentHash);
        }).ToArray();

        var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);

        foreach (var r in readResults) {
            if (!options.Force && storedHashes.TryGetValue(r.FilePath, out var storedHash) && storedHash == r.Hash) {
                skippedCount++;
            } else {
                filesToIndex.Add((r.FilePath, r.SourceCode, r.Hash));
            }
            existingFiles.Add(r.FilePath);
        }

        progress?.Report(new IndexProgress { Current = total, Total = total });

        // Phase D: 并行提取符号
        var phaseDSw = Stopwatch.StartNew();
        var extractionResults = ParallelExtractAll(filesToIndex, ct);
        phaseDSw.Stop();
        Console.Error.WriteLine($"[code-index] Phase D 提取: {filesToIndex.Count} 文件 ({phaseDSw.ElapsedMilliseconds}ms)");

        // Phase E: 批量索引写入
        var phaseESw = Stopwatch.StartNew();
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>(filesToIndex.Count);
        for (var i = 0; i < filesToIndex.Count; i++) {
            var (filePath, sourceCode, hash) = filesToIndex[i];
            batch.Add((filePath, sourceCode, hash, extractionResults[i]));
        }
        await _symbolIndex.IndexFilesBatchAsync(batch, ct).ConfigureAwait(false);
        phaseESw.Stop();
        updatedCount = batch.Count;
        Console.Error.WriteLine($"[code-index] Phase E 写入: {updatedCount} 文件 ({phaseESw.ElapsedMilliseconds}ms)");
        phaseSw.Restart();

        // Phase E2: 向量嵌入
        if (_embeddingIndex is not null) {
            foreach (var b in batch) {
                await _embeddingIndex.RemoveFileAsync(b.FilePath, ct).ConfigureAwait(false);
            }
            var allChunks = new List<ChunkInfo>();
            foreach (var b in batch) {
                var parentLocations = b.Extraction.Symbols
                    .Where(s => IsParentDocumentKind(s.Kind))
                    .Select(s => new LineBasedChunkExtractor.ParentLocation(b.FilePath, s.StartLine, s.EndLine, s.FullyQualifiedName))
                    .ToList();
                allChunks.AddRange(LineBasedChunkExtractor.Extract(
                    b.FilePath, b.SourceCode, b.Extraction.Symbols, parentLocations));
            }
            if (allChunks.Count > 0) {
                await _embeddingIndex.IndexChunksAsync(allChunks, ct).ConfigureAwait(false);
            }
            Console.Error.WriteLine($"[code-index] Phase E2 嵌入: {allChunks.Count} 块 ({phaseSw.ElapsedMilliseconds}ms)");
        }
        phaseSw.Restart();

        // Phase F: 删除已移除文件
        foreach (var trackedFile in trackedFiles) {
            if (!existingFiles.Contains(trackedFile)) {
                await _symbolIndex.RemoveFileAsync(trackedFile, ct).ConfigureAwait(false);
                if (_embeddingIndex is not null) {
                    await _embeddingIndex.RemoveFileAsync(trackedFile, ct).ConfigureAwait(false);
                }
                deletedCount++;
            }
        }

        // Phase G: 失效图缓存
        InvalidateGraphCaches();

        // Phase H: 统一持久化所有 IIndexStore — 并行保存（CLI 单次调用模式跨进程恢复）
        _lastVectorIndexDir = Path.Combine(options.WorkspaceRoot, ".jcc", "code-index");
        var saveTasks = _indexStores.Select(async store => {
            try {
                await store.SaveAsync(_lastVectorIndexDir, ct).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "{Kind}索引持久化失败（内存索引仍可用）", store.Kind);
            }
        }).ToArray();
        await Task.WhenAll(saveTasks).ConfigureAwait(false);
        Console.Error.WriteLine($"[code-index] 持久化: {phaseSw.ElapsedMilliseconds}ms");
        phaseSw.Restart();

        totalSw.Stop();
        Console.Error.WriteLine($"[code-index] 总计: {totalSw.ElapsedMilliseconds}ms");

        return new BuildIndexResult {
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            DeletedCount = deletedCount,
            VectorChunkCount = _embeddingIndex?.ChunkCount ?? 0
        };
    }

    private async Task IndexProjectsAsync(string workspaceRoot, CancellationToken ct) {
        var solutionFiles = CollectFiles(workspaceRoot, "*.slnx")
            .Concat(CollectFiles(workspaceRoot, "*.sln"))
            .ToList();

        foreach (var slnFile in solutionFiles) {
            ct.ThrowIfCancellationRequested();
            try {
                await _projectIndex.IndexSolutionAsync(slnFile, ct).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "CodeIndexer: 解析 solution 文件失败,跳过: {File}", slnFile);
            }
        }

        if (solutionFiles.Count == 0) {
            var csprojFiles = CollectFiles(workspaceRoot, "*.csproj");
            foreach (var csprojFile in csprojFiles) {
                ct.ThrowIfCancellationRequested();
                try {
                    await _projectIndex.IndexProjectAsync(csprojFile, workspaceRoot, ct).ConfigureAwait(false);
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "CodeIndexer: 解析项目文件失败,跳过: {File}", csprojFile);
                }
            }
        }

        _projectDependencyGraph.InvalidateCache();
    }

    private List<ExtractionResult> ParallelExtractAll(
        List<(string FilePath, string SourceCode, string Hash)> files, CancellationToken ct) {
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
            .ForAll(range => {
                LockRegistry.RegisterFlow();
                using var parser = TreeSitterParserPool.CreateDisposable();
                using var extractor = new CSharpSymbolExtractor(parser);
                var mdExtractor = new MarkdownChunkExtractor();

                for (var i = range.Item1; i < range.Item2; i++) {
                    ct.ThrowIfCancellationRequested();
                    var f = files[i];
                    results[i] = f.FilePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        ? mdExtractor.ExtractAll(f.SourceCode, f.FilePath)
                        : extractor.ExtractAll(f.SourceCode, f.FilePath);
                }
            });

        return [.. results];
    }

    /// <summary>处理一批文件 — 符号写入和向量嵌入并行</summary>
    private async Task ProcessBatchAsync(
        List<(string FilePath, string SourceCode, string Hash)> files,
        List<ExtractionResult> extractionResults,
        CancellationToken ct) {
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>(files.Count);
        for (var i = 0; i < files.Count; i++) {
            var (filePath, sourceCode, hash) = files[i];
            batch.Add((filePath, sourceCode, hash, extractionResults[i]));
        }

        var symbolTask = _symbolIndex.IndexFilesBatchAsync(batch, ct);

        var embeddingTask = Task.Run(async () => {
            if (_embeddingIndex is null) return;
            foreach (var b in batch) {
                await _embeddingIndex.RemoveFileAsync(b.FilePath, ct).ConfigureAwait(false);
            }
            var allChunks = new List<ChunkInfo>();
            foreach (var b in batch) {
                var parentLocations = b.Extraction.Symbols
                    .Where(s => IsParentDocumentKind(s.Kind))
                    .Select(s => new LineBasedChunkExtractor.ParentLocation(b.FilePath, s.StartLine, s.EndLine, s.FullyQualifiedName))
                    .ToList();
                allChunks.AddRange(LineBasedChunkExtractor.Extract(
                    b.FilePath, b.SourceCode, b.Extraction.Symbols, parentLocations));
            }
            if (allChunks.Count > 0) {
                await _embeddingIndex.IndexChunksAsync(allChunks, ct).ConfigureAwait(false);
            }
        }, ct);

        await Task.WhenAll(symbolTask, embeddingTask).ConfigureAwait(false);
    }

    /// <summary>
    /// 增量更新单个文件 — 通过 IncrementalUpdater 处理符号索引变更，同步更新向量索引和父文档存储。
    /// <para>.cs 文件：IncrementalUpdater 提取符号+chunks，结果复用到向量索引。</para>
    /// <para>.md 文件：IncrementalUpdater 提取为空（CSharpSymbolExtractor 不支持 md），此处用 MarkdownChunkExtractor 重新提取。</para>
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="ct">取消令牌</param>
    public async Task UpdateFileAsync(string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var result = await _updater.UpdateAsync(filePath, ct).ConfigureAwait(false);
        await InvalidateGraphCachesForFileAsync(filePath, ct).ConfigureAwait(false);

        if (!result.WasUpdated || _embeddingIndex is null) return;

        await _embeddingIndex.RemoveFileAsync(filePath, ct).ConfigureAwait(false);

        if (result.WasDeleted) return;

        var extraction = filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? await ExtractMarkdownAsync(filePath, ct).ConfigureAwait(false)
            : result.Extraction;
        if (extraction is null) return;

        if (extraction.Chunks.Count > 0) {
            await _embeddingIndex.IndexChunksAsync(extraction.Chunks, ct).ConfigureAwait(false);
        }
    }

    private async Task<ExtractionResult?> ExtractMarkdownAsync(string filePath, CancellationToken ct) {
        if (!_fs.FileExists(filePath)) return null;
        var sourceCode = await _fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return new MarkdownChunkExtractor().ExtractAll(sourceCode, filePath);
    }

    /// <summary>
    /// 从索引中移除文件 — 删除符号记录并失效图缓存
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="ct">取消令牌</param>
    public async Task RemoveFileAsync(string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        await _symbolIndex.RemoveFileAsync(filePath, ct).ConfigureAwait(false);
        InvalidateGraphCaches();
    }

    private void InvalidateGraphCaches() {
        _callGraph.InvalidateCache();
        _dependencyGraph.InvalidateCache();
        _projectDependencyGraph.InvalidateCache();
    }

    private async Task InvalidateGraphCachesForFileAsync(string filePath, CancellationToken ct) {
        await _callGraph.InvalidateCacheForFileAsync(filePath, ct).ConfigureAwait(false);
        await _dependencyGraph.InvalidateCacheForFileAsync(filePath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取索引统计信息
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>索引统计快照</returns>
    public async Task<IndexStats> GetStatsAsync(CancellationToken ct) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return await _symbolIndex.GetStatsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 综合检索: rg式模糊匹配符号 → 获取全部函数引用 + 调用方/被调用方,受 token 预算限制
    /// 流程: 模糊匹配 → 收集 references/callers/callees → 按 token 预算截断(优先级: matched > refs > callers > callees)
    /// </summary>
    public async Task<ComprehensiveSearchResult> SearchComprehensiveAsync(string pattern, int maxTokenBudget, CancellationToken ct, bool includeAst = true) {
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

        if (includeAst) {
            foreach (var symbol in allMatched) {
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
        // 先物化去重列表(用于截断计数)
        var distinctReferences = allReferences.Distinct().ToList();
        var distinctCallers = allCallers.Distinct().ToList();
        var distinctCallees = allCallees.Distinct().ToList();

        var (matchedSymbols, references, callers, callees, estimatedTokens, truncated, truncatedCount) =
            TruncateByTokenBudget(allMatched, distinctReferences, distinctCallers, distinctCallees, maxTokenBudget);

        sw.Stop();

        return new ComprehensiveSearchResult {
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
    /// 按 token 预算截断四类别列表 — 优先级: matched > references > callers > callees
    /// <para>纯计算:无 IO 无异步,确定性输出(相同输入永远相同输出)</para>
    /// </summary>
    /// <param name="allMatched">模糊匹配到的符号(原始顺序,未去重)</param>
    /// <param name="distinctReferences">去重后的引用列表</param>
    /// <param name="distinctCallers">去重后的调用方边列表</param>
    /// <param name="distinctCallees">去重后的被调用方边列表</param>
    /// <param name="maxTokenBudget">token 预算上限</param>
    /// <returns>截断后的四列表 + 估算 token 总数 + 是否截断 + 截断条目数</returns>
    internal static (List<SymbolInfo> Matched, List<SymbolInfo> References, List<CallEdge> Callers, List<CallEdge> Callees, int EstimatedTokens, bool Truncated, int TruncatedCount)
        TruncateByTokenBudget(IReadOnlyList<SymbolInfo> allMatched, IReadOnlyList<SymbolInfo> distinctReferences, IReadOnlyList<CallEdge> distinctCallers, IReadOnlyList<CallEdge> distinctCallees, int maxTokenBudget) {
        var matchedSymbols = new List<SymbolInfo>();
        var references = new List<SymbolInfo>();
        var callers = new List<CallEdge>();
        var callees = new List<CallEdge>();
        var estimatedTokens = 0;
        var truncated = false;

        // 填充 matched symbols
        foreach (var s in allMatched) {
            var t = EstimateSymbolTokens(s);
            if (estimatedTokens + t > maxTokenBudget) {
                truncated = true;
                break;
            }
            matchedSymbols.Add(s);
            estimatedTokens += t;
        }

        // 填充 references (去重)
        foreach (var r in distinctReferences) {
            var t = EstimateSymbolTokens(r);
            if (estimatedTokens + t > maxTokenBudget) {
                truncated = true;
                break;
            }
            references.Add(r);
            estimatedTokens += t;
        }

        // 填充 callers (去重)
        foreach (var c in distinctCallers) {
            var t = EstimateEdgeTokens(c);
            if (estimatedTokens + t > maxTokenBudget) {
                truncated = true;
                break;
            }
            callers.Add(c);
            estimatedTokens += t;
        }

        // 填充 callees (去重)
        foreach (var c in distinctCallees) {
            var t = EstimateEdgeTokens(c);
            if (estimatedTokens + t > maxTokenBudget) {
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

        return (matchedSymbols, references, callers, callees, estimatedTokens, truncated, truncatedCount);
    }

    /// <summary>
    /// 估算符号的 token 数 — 约 4 字符/token,符号含 Name+FQN+FilePath 等
    /// </summary>
    internal static int EstimateSymbolTokens(SymbolInfo symbol) {
        // 简化估算: Name + FQN + FilePath 字符数 / 4, 最低 5 tokens
        var chars = symbol.Name.Length + symbol.FullyQualifiedName.Length + symbol.FilePath.Length;
        return Math.Max(5, chars / 4);
    }

    /// <summary>
    /// 估算调用边的 token 数 — Caller + Callee + FilePath 等
    /// </summary>
    internal static int EstimateEdgeTokens(CallEdge edge) {
        var chars = edge.CallerSymbol.Length + edge.CalleeSymbol.Length + edge.CallSiteFilePath.Length;
        return Math.Max(4, chars / 4);
    }

    private IReadOnlyList<string> CollectCsFiles(string workspaceRoot, IEnumerable<string>? excludePatterns) {
        // 默认排除 bin/obj/.git/.x — 避免扫描编译产物和临时目录
        var excludes = (excludePatterns ?? [])
            .Select(p => p.TrimEnd('/', '\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 强制加入 bin/obj/.git/.x (用户明确要求跳过 bin/obj) — 委托 CodeIndexExcludedDirCatalog
        foreach (var forced in CodeIndexExcludedDirCatalog.DefaultExcludedDirs) {
            excludes.Add(forced);
        }

        var result = new List<string>();
        CollectCsFilesRecursive(workspaceRoot, workspaceRoot, excludes, result);
        return result;
    }

    private void CollectCsFilesRecursive(string currentDir, string workspaceRoot, HashSet<string> excludes, List<string> result) {
        try {
            foreach (var dir in _fs.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly)) {
                var span = dir.AsSpan();
                var lastSep = span.LastIndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dirName = lastSep >= 0 ? span[(lastSep + 1)..] : span;

                if (excludes.Contains(dirName.ToString())) {
                    continue;
                }

                CollectCsFilesRecursive(dir, workspaceRoot, excludes, result);
            }

            foreach (var file in _fs.EnumerateFiles(currentDir, "*.cs", SearchOption.TopDirectoryOnly)) {
                result.Add(file);
            }
            foreach (var file in _fs.EnumerateFiles(currentDir, "*.md", SearchOption.TopDirectoryOnly)) {
                result.Add(file);
            }
        } catch (UnauthorizedAccessException ex) { _logger?.LogWarning(ex, "CodeIndexer: 扫描目录时访问被拒绝"); }
    }

    private List<string> CollectFiles(string workspaceRoot, string pattern) {
        var result = new List<string>();
        try {
            foreach (var file in _fs.EnumerateFiles(workspaceRoot, pattern, SearchOption.TopDirectoryOnly)) {
                result.Add(file);
            }
        } catch (UnauthorizedAccessException ex) { _logger?.LogWarning(ex, "CodeIndexer: 按模式 {Pattern} 收集文件时访问被拒绝", pattern); }
        return result;
    }

    private Dictionary<string, string> BatchGetStoredHashes(IReadOnlyList<string> filePaths) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (filePaths.Count == 0) return result;

        var snap = _store.GetSnapshot();
        foreach (var fp in filePaths) {
            if (snap.FileTracking.TryGetValue(fp, out var entry)) {
                result[fp] = entry.Hash;
            }
        }
        return result;
    }

    private IReadOnlyList<string> GetTrackedFilesInWorkspace(string workspaceRoot) {
        var snap = _store.GetSnapshot();
        return snap.FileTracking.Keys
            .Where(p => p.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 确保索引已加载 — 统一加载符号索引(IKvStore) + 向量索引(LSM-Tree) + 父文档(IKvStore)。
    /// persistDir 为 null 时自动发现 .git 工作区根；不为 null 时从指定目录加载。
    /// 用 Interlocked 保证只执行一次。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <param name="persistDir">持久化目录路径(null 时自动发现 git 工作区根)。</param>
    public async Task EnsureIndexLoadedAsync(CancellationToken ct, string? persistDir = null) {
        if (Interlocked.CompareExchange(ref _autoLoadState, 1, 0) != 0) return;

        try {
            await EnsureEmbeddingModelAsync(ct).ConfigureAwait(false);
            string dir;
            if (persistDir is not null) {
                dir = persistDir;
                _logger?.LogDebug("CodeIndexer: EnsureIndexLoadedAsync — 使用外部持久化路径 {Dir}", dir);
            } else {
                _logger?.LogDebug("CodeIndexer: EnsureIndexLoadedAsync — FindGitWorkspaceDir...");
                var root = GitWorkspaceResolver.FindGitWorkspaceDir(null, _fs);
                if (root is null) {
                    _logger?.LogDebug("CodeIndexer: 未发现 .git 工作区根,跳过自动加载");
                    return;
                }
                _autoDiscoveredWorkspaceRoot = root;
                dir = Path.Combine(root, AutoLoadSubDir);
            }

            _lastVectorIndexDir = dir;

            // 统一加载所有 IIndexStore — LINQ 链式，不再写三段 if
            foreach (var store in _indexStores) {
                ct.ThrowIfCancellationRequested();
                if (store.IsReady) continue;
                if (!await store.ExistsAsync(dir, ct).ConfigureAwait(false)) continue;
                try {
                    var loaded = await store.LoadAsync(dir, ct).ConfigureAwait(false);
                    if (loaded) {
                        _logger?.LogInformation("CodeIndexer: 自动加载{Kind}索引成功 from {Dir}", store.Kind, dir);
                    } else {
                        _logger?.LogDebug("CodeIndexer: {Kind}索引版本不匹配或为空 {Dir}", store.Kind, dir);
                    }
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "CodeIndexer: {Kind}索引加载失败 {Dir}", store.Kind, dir);
                }
            }

            _logger?.LogDebug("CodeIndexer: EnsureIndexLoadedAsync 完成 — 索引为空时请用 /index 命令显式构建");
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "CodeIndexer: 自动加载索引失败");
        }
    }

    /// <summary>
    /// 显式重建索引并持久化到磁盘 — 供斜杠命令 /index 调用
    /// </summary>
    public async Task RebuildIndexAsync(CancellationToken ct) {
        var root = _autoDiscoveredWorkspaceRoot ?? GitWorkspaceResolver.FindGitWorkspaceDir(null, _fs);
        if (root is null) {
            _logger?.LogWarning("CodeIndexer: 未发现 .git 工作区根,无法重建索引");
            return;
        }

        var dir = Path.Combine(root, AutoLoadSubDir);
        _logger?.LogInformation("CodeIndexer: 显式重建工作区索引 {Root}", root);
        await RebuildAndPersistAsync(root, dir, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 重建索引并持久化到磁盘
    /// </summary>
    private async Task RebuildAndPersistAsync(string root, string dir, CancellationToken ct) {
        var options = new CodeIndexOptions { WorkspaceRoot = root };
        await BuildIndexAsync(options, ct).ConfigureAwait(false);
        try {
            await _persistence.SaveAsync(dir, ct).ConfigureAwait(false);
            _logger?.LogInformation("CodeIndexer: 重建完成并持久化到 {Dir}", dir);
        } catch (Exception persistEx) {
            _logger?.LogWarning(persistEx, "CodeIndexer: 重建后持久化失败(内存索引仍可用)");
        }
    }

    /// <summary>
    /// 检查索引是否过时 — 自动适配主仓库(.git/目录)和 worktree(.git/文件)，对笨蛋用户透明
    /// </summary>
    private async Task<bool> IsIndexStaleAsync(string workspaceRoot) {
        try {
            var gitPath = _fs.CombinePath(workspaceRoot, ".git");

            if (_fs.DirectoryExists(gitPath)) {
                return IsFileStale(_fs.CombinePath(gitPath, "HEAD"));
            }

            if (_fs.FileExists(gitPath)) {
                var gitDir = await ParseGitFileAsync(gitPath).ConfigureAwait(false);
                if (gitDir is not null) {
                    return IsFileStale(_fs.CombinePath(gitDir, "HEAD"));
                }
                _logger?.LogDebug("CodeIndexer: worktree .git 文件解析 gitdir 失败,降级检查 .git 文件修改时间");
                return IsFileStale(gitPath);
            }
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "CodeIndexer: 检查索引新鲜度失败");
        }
        return false;

        bool IsFileStale(string path) {
            if (!_fs.FileExists(path)) return false;
            return _fs.GetLastWriteTimeUtc(path) > _store.GetSnapshot().LastUpdated;
        }
    }

    /// <summary>
    /// 解析 worktree .git 指针文件内容 — 格式: "gitdir: /path/to/main/.git/worktrees/w1"
    /// </summary>
    private async Task<string?> ParseGitFileAsync(string gitFilePath) {
        try {
            var content = (await _fs.ReadAllText(gitFilePath).ConfigureAwait(false)).Trim();
            const string prefix = "gitdir:";
            if (content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                var gitDir = content[prefix.Length..].Trim();
                if (!Path.IsPathRooted(gitDir)) {
                    gitDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(gitFilePath)!, gitDir));
                }
                return gitDir;
            }
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "CodeIndexer: 解析 .git 指针文件失败");
        }
        return null;
    }

    /// <summary>
    /// 释放资源 — 释放增量更新器和符号索引
    /// </summary>
    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        _updater.Dispose();
        _symbolIndex.Dispose();
        _embeddingIndex?.Dispose();
        _embeddingIndex = null;
        _analytics.Dispose();
        _visualization.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// 判断符号类型是否可作为父文档（类级别容器类型）。
    /// </summary>
    private static bool IsParentDocumentKind(SymbolKind kind) =>
        kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
            or SymbolKind.Record or SymbolKind.RecordStruct or SymbolKind.Enum or SymbolKind.Delegate;
}