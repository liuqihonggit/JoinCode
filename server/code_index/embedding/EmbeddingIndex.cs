namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// 向量索引 — 串联嵌入模型、向量存储、ANN 搜索。
/// <para>维护块哈希缓存：块没变就不重新嵌入（省 API 调用/计算）。</para>
/// <para>无锁并发安全：读操作（Search）完全无锁；写操作（Index/Remove）CAS 路径复制。</para>
/// <para>_ann 自身线程安全（BruteForceAnn 内部有锁），EmbeddingIndex 不再加全局锁。</para>
/// <para>持久化通过 IEmbeddingPersistence 多态委托，支持 bin(mmap) 和 LSM(PithosDB) 两种后端。</para>
/// </summary>
public sealed class EmbeddingIndex : IAsyncDisposable, IIndexStore {

    private readonly IEmbeddingModel _embedModel;
    private readonly IAnnSearch _ann;
    private readonly IFileSystem _fs;
    private readonly IEmbeddingPersistence _persistence;
    private string? _incrementalDir;

    private volatile ImmutableHamT<string, ChunkMetadata> _metadata = ImmutableHamT<string, ChunkMetadata>.Empty;
    private volatile ImmutableHamT<string, string> _chunkHashes = ImmutableHamT<string, string>.Empty;
    private volatile ImmutableHamT<string, ImmutableHashSet<string>> _fileToChunks = ImmutableHamT<string, ImmutableHashSet<string>>.Empty;
    private volatile ImmutableHamT<string, float[]> _vectors = ImmutableHamT<string, float[]>.Empty;
    private volatile int _status;
    private int _disposed;

    /// <summary>
    /// 构造向量索引。
    /// </summary>
    /// <param name="embedModel">嵌入模型（API/ONNX/simhash 均可）。</param>
    /// <param name="ann">ANN 搜索引擎（暴力/HNSW 均可，需自身线程安全）。</param>
    /// <param name="fs">文件系统抽象 — 用于持久化读写 + 父文档源码按行读取。</param>
    public EmbeddingIndex(IEmbeddingModel embedModel, IAnnSearch ann, IFileSystem fs) {
        ArgumentNullException.ThrowIfNull(embedModel);
        ArgumentNullException.ThrowIfNull(ann);
        ArgumentNullException.ThrowIfNull(fs);
        _embedModel = embedModel;
        _ann = ann;
        _fs = fs;
        _persistence = new LsmEmbeddingPersistence(fs);
        _status = (int)IndexStatus.NotReady;
    }

    /// <summary>索引就绪状态 — 供 Agent 层判断查询路径。</summary>
    public IndexStatus Status => (IndexStatus)_status;

    /// <summary>当前已索引的块数量。无锁读取。</summary>
    public int ChunkCount => _metadata.Count;

    /// <summary>索引类型标识。</summary>
    public IndexKind Kind => IndexKind.Vector;

    /// <summary>当前索引项数量（接口统一）。</summary>
    public int Count => ChunkCount;

    /// <summary>是否已就绪可查询。</summary>
    public bool IsReady => Status is IndexStatus.Ready or IndexStatus.Partial;

    /// <summary>
    /// 批量索引代码块 — 跳过哈希未变的块。
    /// <para>首次索引后状态变为 Ready；部分失败则 Partial；全部失败则 Error。</para>
    /// </summary>
    public async Task IndexChunksAsync(IReadOnlyList<ChunkInfo> chunks, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0) return;

        var toEmbed = new List<ChunkInfo>();
        var unchanged = new List<ChunkInfo>();

        var hashesSnapshot = _chunkHashes;
        foreach (var chunk in chunks) {
            ct.ThrowIfCancellationRequested();
            if (hashesSnapshot.TryGetValue(chunk.ChunkId, out var existingHash)
                && existingHash == chunk.ContentHash) {
                unchanged.Add(chunk);
            } else {
                toEmbed.Add(chunk);
            }
        }

        if (toEmbed.Count == 0) {
            Interlocked.Exchange(ref _status, (int)IndexStatus.Ready);
            return;
        }

        const int EmbedBatchSize = 256;
        var vectors = new float[toEmbed.Count][];
        try {
            for (var batchStart = 0; batchStart < toEmbed.Count; batchStart += EmbedBatchSize) {
                ct.ThrowIfCancellationRequested();
                var batchEnd = Math.Min(batchStart + EmbedBatchSize, toEmbed.Count);
                var batchTexts = new List<string>(batchEnd - batchStart);
                for (var i = batchStart; i < batchEnd; i++) {
                    batchTexts.Add(BuildEmbedText(toEmbed[i]));
                }
                var batchVectors = await _embedModel.EmbedBatchAsync(batchTexts, ct).ConfigureAwait(false);
                for (var i = 0; i < batchVectors.Length; i++) {
                    vectors[batchStart + i] = batchVectors[i];
                }
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            Interlocked.Exchange(ref _status, (int)IndexStatus.Error);
            throw;
        }

        var successCount = 0;
        var metadataToAdd = new List<(string ChunkId, ChunkMetadata Meta)>(toEmbed.Count);
        var hashesToAdd = new List<(string ChunkId, string Hash)>(toEmbed.Count);
        var fileChunksToAdd = new List<(string FilePath, string ChunkId)>(toEmbed.Count);

        for (var i = 0; i < toEmbed.Count; i++) {
            ct.ThrowIfCancellationRequested();
            var chunk = toEmbed[i];
            var vector = vectors[i];

            _ann.Add(chunk.ChunkId, vector);
            metadataToAdd.Add((chunk.ChunkId, new ChunkMetadata {
                ChunkId = chunk.ChunkId,
                FilePath = chunk.FilePath,
                SymbolFqn = chunk.SymbolFqn,
                SymbolKind = chunk.Kind.ToString(),
                StartLine = chunk.StartLine,
                EndLine = chunk.EndLine,
                ParentFilePath = chunk.ParentFilePath,
                ParentStartLine = chunk.ParentStartLine,
                ParentEndLine = chunk.ParentEndLine,
                ParentSymbolFqn = chunk.ParentSymbolFqn,
                SourceText = chunk.SourceText,
                ContainedSymbolFqns = chunk.ContainedSymbolFqns,
                ContainedSymbolKinds = chunk.ContainedSymbolKinds
            }));
            hashesToAdd.Add((chunk.ChunkId, chunk.ContentHash));
            fileChunksToAdd.Add((chunk.FilePath, chunk.ChunkId));
            successCount++;
        }

        CasBulkUpdateMetadata(metadataToAdd);
        CasBulkUpdateHashes(hashesToAdd);
        CasBulkUpdateFileChunks(fileChunksToAdd);
        var vectorsToAdd = new List<(string ChunkId, float[] Vector)>(toEmbed.Count);
        for (var i = 0; i < toEmbed.Count; i++) {
            vectorsToAdd.Add((toEmbed[i].ChunkId, vectors[i]));
        }
        CasBulkUpdateVectors(vectorsToAdd);

        var newStatus = successCount == toEmbed.Count
            ? IndexStatus.Ready
            : successCount > 0
                ? IndexStatus.Partial
                : IndexStatus.Error;
        Interlocked.Exchange(ref _status, (int)newStatus);
    }

    /// <summary>
    /// 构建嵌入文本 — 符号短名拆分小写前缀 + 块原文，确保核心语义在前 32 token 内。
    /// <para>例: ContainedSymbolFqns=["A.B.C.CosineSimilarity"] → "cosine similarity\n" + SourceText。</para>
    /// <para>PascalCase 拆分: BERT tokenizer 不拆 PascalCase,需手动拆为小写词。</para>
    /// </summary>
    private static string BuildEmbedText(ChunkInfo chunk) {
        var prefix = SplitPascalCase(chunk.SymbolFqn);
        if (chunk.ContainedSymbolFqns.Count > 0) {
            var parts = new List<string>(chunk.ContainedSymbolFqns.Count);
            foreach (var fqn in chunk.ContainedSymbolFqns) {
                var lastDot = fqn.LastIndexOf('.');
                var shortName = lastDot >= 0 && lastDot < fqn.Length - 1 ? fqn[(lastDot + 1)..] : fqn;
                parts.Add(SplitPascalCase(shortName));
            }
            prefix = string.Join(' ', parts);
        }
        return prefix + "\n" + (chunk.SourceText ?? string.Empty);
    }

    /// <summary>PascalCase 拆分为小写词 — "CosineSimilarity" → "cosine similarity"。</summary>
    private static string SplitPascalCase(string s) {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length * 2);
        for (var i = 0; i < s.Length; i++) {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) {
                sb.Append(' ');
            }
            sb.Append(char.ToLowerInvariant(s[i]));
        }
        return sb.ToString();
    }

    private void CasBulkUpdateMetadata(List<(string ChunkId, ChunkMetadata Meta)> items) {
        if (items.Count == 0) return;
        var spin = new SpinWait();
        while (true) {
            var current = _metadata;
            var updated = current;
            foreach (var (chunkId, meta) in items) {
                updated = updated.SetItem(chunkId, meta);
            }
            if (Interlocked.CompareExchange(ref _metadata, updated, current) == current) break;
            spin.SpinOnce();
        }
    }

    private void CasBulkUpdateHashes(List<(string ChunkId, string Hash)> items) {
        if (items.Count == 0) return;
        var spin = new SpinWait();
        while (true) {
            var current = _chunkHashes;
            var updated = current;
            foreach (var (chunkId, hash) in items) {
                updated = updated.SetItem(chunkId, hash);
            }
            if (Interlocked.CompareExchange(ref _chunkHashes, updated, current) == current) break;
            spin.SpinOnce();
        }
    }

    private void CasBulkUpdateFileChunks(List<(string FilePath, string ChunkId)> items) {
        if (items.Count == 0) return;
        var spin = new SpinWait();
        while (true) {
            var current = _fileToChunks;
            var updated = current;
            foreach (var (filePath, chunkId) in items) {
                var existing = updated.TryGetValue(filePath, out var set) ? set : ImmutableHashSet<string>.Empty;
                updated = updated.SetItem(filePath, existing.Add(chunkId));
            }
            if (Interlocked.CompareExchange(ref _fileToChunks, updated, current) == current) break;
            spin.SpinOnce();
        }
    }

    private void CasBulkUpdateVectors(List<(string ChunkId, float[] Vector)> items) {
        if (items.Count == 0) return;
        var spin = new SpinWait();
        while (true) {
            var current = _vectors;
            var updated = current;
            foreach (var (chunkId, vector) in items) {
                updated = updated.SetItem(chunkId, vector);
            }
            if (Interlocked.CompareExchange(ref _vectors, updated, current) == current) break;
            spin.SpinOnce();
        }
    }

    /// <summary>
    /// 持久化向量索引 — 委托 IEmbeddingPersistence 多态后端。
    /// <para>环境变量 JCC_EMBEDDING_BACKEND=bin 用 mmap 二进制，=lsm 用 PithosDB LSM-Tree（默认）。</para>
    /// </summary>
    /// <param name="dirPath">目标目录路径。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task SaveAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var chunks = CollectChunksForSave();
        var dims = chunks.Count > 0 ? chunks[0].Vector.Length : 0;
        var graphBytes = BuildGraphBytes();
        var snapshot = new EmbeddingSnapshot {
            Dims = dims,
            Chunks = chunks,
            GraphBytes = graphBytes
        };
        await _persistence.SaveAsync(dirPath, snapshot, ct).ConfigureAwait(false);
    }

    /// <summary>收集待持久化的块快照 — 向量+元数据+哈希三元组对齐。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<(string ChunkId, float[] Vector, ChunkMetadata Meta, string Hash)> CollectChunksForSave() {
        var vectorsSnapshot = _vectors;
        var metadataSnapshot = _metadata;
        var hashesSnapshot = _chunkHashes;
        var chunks = new List<(string, float[], ChunkMetadata, string)>(vectorsSnapshot.Count);
        foreach (var (chunkId, vector) in vectorsSnapshot) {
            if (!metadataSnapshot.TryGetValue(chunkId, out var meta)) continue;
            hashesSnapshot.TryGetValue(chunkId, out var hash);
            chunks.Add((chunkId, vector, meta, hash ?? string.Empty));
        }
        return chunks;
    }

    /// <summary>构建图段字节 — IAnnSearchGraphPersistence 时序列化图，否则 null。</summary>
    private byte[]? BuildGraphBytes() {
        if (_ann is not IAnnSearchGraphPersistence graphPersist) return null;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8);
        bw.Write((byte)1);
        graphPersist.SaveGraph(bw);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// 加载向量索引 — 委托 IEmbeddingPersistence 多态后端，填充内存索引 + 恢复 ANN 图。
    /// </summary>
    /// <param name="dirPath">源目录路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示加载成功。</returns>
    public async Task<bool> LoadAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var snapshot = await _persistence.LoadAsync(dirPath, ct).ConfigureAwait(false);
        if (snapshot is null) return false;
        ApplySnapshot(snapshot);
        RestoreAnnGraph(snapshot);
        Interlocked.Exchange(ref _status, (int)IndexStatus.Ready);
        return true;
    }

    /// <summary>将快照填充到内存索引字段 — CAS 批量更新 metadata/vectors/hashes/fileToChunks。</summary>
    private void ApplySnapshot(EmbeddingSnapshot snapshot) {
        var metadataBuilder = ImmutableHamT.CreateBuilder<string, ChunkMetadata>();
        var hashBuilder = ImmutableHamT.CreateBuilder<string, string>();
        var vectorBuilder = ImmutableHamT.CreateBuilder<string, float[]>();
        var fileToChunksBuilder = ImmutableHamT.CreateBuilder<string, ImmutableHashSet<string>>();

        foreach (var (chunkId, vector, meta, hash) in snapshot.Chunks) {
            vectorBuilder.Add(chunkId, vector);
            metadataBuilder.Add(chunkId, meta);
            fileToChunksBuilder.TryGetValue(meta.FilePath, out var existing);
            fileToChunksBuilder[meta.FilePath] = (existing ?? ImmutableHashSet<string>.Empty).Add(chunkId);
            hashBuilder.Add(chunkId, hash);
        }

        Interlocked.Exchange(ref _metadata, metadataBuilder.ToImmutable());
        Interlocked.Exchange(ref _chunkHashes, hashBuilder.ToImmutable());
        Interlocked.Exchange(ref _vectors, vectorBuilder.ToImmutable());
        Interlocked.Exchange(ref _fileToChunks, fileToChunksBuilder.ToImmutable());
    }

    /// <summary>恢复 ANN 图 — 有图段字节时 LoadGraph，否则遍历 vectors 逐个 Add。</summary>
    private void RestoreAnnGraph(EmbeddingSnapshot snapshot) {
        if (snapshot.GraphBytes is { } graphBytes && graphBytes.Length > 0
            && _ann is IAnnSearchGraphPersistence gp) {
            using var ms = new MemoryStream(graphBytes);
            using var br = new BinaryReader(ms, Encoding.UTF8);
            var hasGraph = br.ReadByte();
            if (hasGraph == 1) {
                var vectorsDict = new Dictionary<string, float[]>(snapshot.Chunks.Count);
                foreach (var (id, vec, _, _) in snapshot.Chunks) {
                    vectorsDict[id] = vec;
                }
                gp.LoadGraph(br, vectorsDict);
                return;
            }
        }
        foreach (var (id, vec, _, _) in snapshot.Chunks) {
            _ann.Add(id, vec);
        }
    }

    /// <summary>检查指定目录是否存在持久化数据 — 委托后端。</summary>
    public Task<bool> ExistsAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        return _persistence.ExistsAsync(dirPath, ct);
    }

    /// <summary>
    /// 增量写入单个 chunk — 直接追加到存储，无需全量重建索引。
    /// <para>bin 后端超过千万行时自动切换到 LSM 后端。</para>
    /// <para>无需显式打开，直接可用。</para>
    /// </summary>
    internal async Task PersistChunkAsync(string dirPath, string chunkId, float[] vector, ChunkMetadata meta, string hash, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(dirPath);
        ArgumentNullException.ThrowIfNull(chunkId);
        _incrementalDir = dirPath;
        await _persistence.PersistChunkAsync(dirPath, chunkId, vector, meta, hash, ct).ConfigureAwait(false);
    }

    /// <summary>增量删除单个 chunk — 从存储移除，无需全量重建。无需显式打开。</summary>
    public Task DeleteChunkAsync(string dirPath, string chunkId, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(dirPath);
        ArgumentNullException.ThrowIfNull(chunkId);
        _incrementalDir = dirPath;
        return _persistence.DeleteChunkAsync(dirPath, chunkId, ct);
    }

    /// <summary>刷盘 — LSM 每次 PutAsync 已持久化，空操作。</summary>
    public Task FlushAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        return _persistence.FlushAsync(dirPath, ct);
    }

    /// <summary>
    /// 按偏移量从索引文件读取 SourceText — LSM 后端 SourceText 已在 meta 中，直接返回。
    /// </summary>
    private static Task<string?> ReadSourceTextAsync(ChunkMetadata meta, CancellationToken ct) {
        return Task.FromResult(meta.SourceText);
    }

    /// <summary>
    /// 语义搜索 — 查询文本 → 嵌入 → ANN → 元数据。
    /// <para>索引未就绪时返回空列表（调用方可降级到符号搜索）。</para>
    /// <para>options.IncludeSourceText=true 时结果携带块原文；IncludeParentDocument=false 时不返回父文档。</para>
    /// </summary>
    public async Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        string query, int topK, CancellationToken ct, SearchOptions? options = null) {
        ArgumentNullException.ThrowIfNull(query);
        if (topK <= 0) return [];
        if (Status == IndexStatus.NotReady || Status == IndexStatus.Error) return [];

        float[] queryVector;
        try {
            queryVector = await _embedModel.EmbedAsync(SplitPascalCase(query), ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            return [];
        }

        var fileType = options?.FileType;
        var namespaceFilter = options?.Namespace;
        var symbolKindFilter = options?.SymbolKind;
        var hasFilter = !string.IsNullOrEmpty(fileType)
            || !string.IsNullOrEmpty(namespaceFilter)
            || !string.IsNullOrEmpty(symbolKindFilter);
        var oversampleK = hasFilter ? topK * 3 : topK;
        var annResults = _ann.Search(queryVector, oversampleK, ct);
        if (annResults.Count == 0) return [];

        var metadataSnapshot = _metadata;
        var results = new List<ChunkSearchResult>(annResults.Count);
        var includeSource = options?.IncludeSourceText ?? false;
        var includeParent = options?.IncludeParentDocument ?? false;
        foreach (var (id, score) in annResults) {
            if (!metadataSnapshot.TryGetValue(id, out var meta)) continue;
            if (hasFilter) {
                if (!string.IsNullOrEmpty(fileType) && !MatchesFileType(meta.FilePath, fileType!)) continue;
                if (!string.IsNullOrEmpty(namespaceFilter) && !MatchesNamespaceAny(meta.SymbolFqn, meta.ContainedSymbolFqns, namespaceFilter!)) continue;
                if (!string.IsNullOrEmpty(symbolKindFilter) && !MatchesSymbolKindAny(meta.SymbolKind, meta.ContainedSymbolKinds, symbolKindFilter!)) continue;
            }
            var result = new ChunkSearchResult {
                ChunkId = meta.ChunkId,
                FilePath = meta.FilePath,
                SymbolFqn = meta.SymbolFqn,
                StartLine = meta.StartLine,
                EndLine = meta.EndLine,
                Score = score,
                SourceText = includeSource ? await ReadSourceTextAsync(meta, ct).ConfigureAwait(false) : null,
                ContainedSymbolFqns = meta.ContainedSymbolFqns
            };
            results.Add(includeParent
                ? await TryAttachParentDocumentAsync(result, meta, ct).ConfigureAwait(false)
                : result);
            if (results.Count >= topK) break;
        }
        return results;
    }

    /// <summary>
    /// 检查文件路径是否匹配指定类型（扩展名比较，不区分大小写）。
    /// </summary>
    private static bool MatchesFileType(string filePath, string fileType) {
        var ext = Path.GetExtension(filePath.AsSpan());
        if (ext.Length == 0) return false;
        return ext[1..].Equals(fileType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查符号 FQN 是否属于指定命名空间（前缀匹配）。
    /// </summary>
    private static bool MatchesNamespace(string fqn, string namespaceFilter) {
        return fqn.StartsWith(namespaceFilter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查符号 FQN 或其包含的符号 FQN 是否属于指定命名空间（前缀匹配）。
    /// </summary>
    private static bool MatchesNamespaceAny(string fqn, IReadOnlyList<string> containedFqns, string namespaceFilter) {
        if (MatchesNamespace(fqn, namespaceFilter)) return true;
        foreach (var cfqn in containedFqns) {
            if (MatchesNamespace(cfqn, namespaceFilter)) return true;
        }
        return false;
    }

    /// <summary>
    /// 检查符号类型是否匹配 — 优先用 ContainedSymbolKinds BitMask 检查块内所有符号类型，回退到 SymbolKind 字符串比较。
    /// <para>BitMask 路径：LineBasedChunkExtractor 块的 Kind 全是 Document，但 ContainedSymbolKinds 记录了块内所有符号类型。</para>
    /// <para>字符串路径：CollectChunksWithParents 块的 Kind 是真实符号类型，ContainedSymbolKinds 为 0。</para>
    /// </summary>
    private static bool MatchesSymbolKindAny(string symbolKind, int containedSymbolKinds, string symbolKindFilter) {
        var kind = SymbolKindExtensions.FromValue(symbolKindFilter);
        if (kind is not null) {
            return BitMask.Contains(containedSymbolKinds, kind.Value);
        }
        return symbolKind.Equals(symbolKindFilter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 尝试附加父文档原文到搜索结果 — 从文件系统按行范围读取父文档源码。
    /// <para>父文档定位信息（FilePath/StartLine/EndLine/SymbolFqn）存在 ChunkMetadata 中，搜索时零额外存储。</para>
    /// </summary>
    private async Task<ChunkSearchResult> TryAttachParentDocumentAsync(ChunkSearchResult result, ChunkMetadata meta, CancellationToken ct) {
        if (meta.ParentFilePath is null) return result;
        if (!_fs.FileExists(meta.ParentFilePath)) return result;
        string[] allLines;
        try {
            allLines = await _fs.ReadAllLinesAsync(meta.ParentFilePath, ct).ConfigureAwait(false);
        } catch {
            return result;
        }
        var start = Math.Max(0, meta.ParentStartLine - 1);
        var end = Math.Min(allLines.Length, meta.ParentEndLine);
        if (end <= start) return result;
        var parentLines = allLines[start..end];
        const int maxLines = 2000;
        if (parentLines.Length > maxLines) {
            parentLines = parentLines[..maxLines];
        }
        return result with {
            ParentDocumentText = string.Join('\n', parentLines),
            ParentStartLine = meta.ParentStartLine,
            ParentEndLine = meta.ParentEndLine,
            ParentSymbolFqn = meta.ParentSymbolFqn
        };
    }

    /// <summary>
    /// 删除文件关联的所有块。
    /// </summary>
    public Task RemoveFileAsync(string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!_fileToChunks.TryGetValue(filePath, out var chunkIds)) {
            return Task.CompletedTask;
        }
        foreach (var chunkId in chunkIds) {
            _ann.Remove(chunkId);
        }
        var spin = new SpinWait();
        while (true) {
            var current = _metadata;
            var updated = current.RemoveRange(chunkIds);
            if (Interlocked.CompareExchange(ref _metadata, updated, current) == current) break;
            spin.SpinOnce();
        }
        while (true) {
            var current = _chunkHashes;
            var updated = current.RemoveRange(chunkIds);
            if (Interlocked.CompareExchange(ref _chunkHashes, updated, current) == current) break;
            spin.SpinOnce();
        }
        while (true) {
            var current = _fileToChunks;
            var updated = current.Remove(filePath);
            if (Interlocked.CompareExchange(ref _fileToChunks, updated, current) == current) break;
            spin.SpinOnce();
        }
        while (true) {
            var current = _vectors;
            var updated = current.RemoveRange(chunkIds);
            if (Interlocked.CompareExchange(ref _vectors, updated, current) == current) break;
            spin.SpinOnce();
        }
        if (_metadata.Count == 0) {
            Interlocked.Exchange(ref _status, (int)IndexStatus.NotReady);
        }
        return Task.CompletedTask;
    }

    /// <summary>清空索引。</summary>
    public void Clear() {
        _metadata = ImmutableHamT<string, ChunkMetadata>.Empty;
        _chunkHashes = ImmutableHamT<string, string>.Empty;
        _fileToChunks = ImmutableHamT<string, ImmutableHashSet<string>>.Empty;
        Interlocked.Exchange(ref _status, (int)IndexStatus.NotReady);
    }

    /// <summary>释放资源（无锁实现，标记已释放）。</summary>
    public void Dispose() {
        Interlocked.Exchange(ref _disposed, 1);
    }

    /// <summary>异步释放资源 — 关闭持久化后端。</summary>
    public async ValueTask DisposeAsync() {
        Interlocked.Exchange(ref _disposed, 1);
        await _persistence.DisposeAsync().ConfigureAwait(false);
    }
}
