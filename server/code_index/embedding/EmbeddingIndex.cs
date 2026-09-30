namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// 向量索引 — 串联嵌入模型、向量存储、ANN 搜索。
/// <para>维护块哈希缓存：块没变就不重新嵌入（省 API 调用/计算）。</para>
/// <para>无锁并发安全：读操作（Search）完全无锁；写操作（Index/Remove）CAS 路径复制。</para>
/// <para>_ann 自身线程安全（BruteForceAnn 内部有锁），EmbeddingIndex 不再加全局锁。</para>
/// </summary>
public sealed class EmbeddingIndex : IAsyncDisposable {

    private readonly IEmbeddingModel _embedModel;
    private readonly IAnnSearch _ann;
    private readonly IParentDocumentStore? _parentStore;
    private volatile ImmutableHamT<string, ChunkMetadata> _metadata = ImmutableHamT<string, ChunkMetadata>.Empty;
    private volatile ImmutableHamT<string, string> _chunkHashes = ImmutableHamT<string, string>.Empty;
    private volatile ImmutableHamT<string, ImmutableHashSet<string>> _fileToChunks = ImmutableHamT<string, ImmutableHashSet<string>>.Empty;
    private volatile int _status;
    private int _disposed;

    /// <summary>
    /// 构造向量索引。
    /// </summary>
    /// <param name="embedModel">嵌入模型（API/ONNX/simhash 均可）。</param>
    /// <param name="ann">ANN 搜索引擎（暴力/HNSW 均可，需自身线程安全）。</param>
    /// <param name="parentStore">父文档存储（可选）— 注入后 SearchAsync 返回结果携带父文档原文。</param>
    public EmbeddingIndex(IEmbeddingModel embedModel, IAnnSearch ann, IParentDocumentStore? parentStore = null) {
        ArgumentNullException.ThrowIfNull(embedModel);
        ArgumentNullException.ThrowIfNull(ann);
        _embedModel = embedModel;
        _ann = ann;
        _parentStore = parentStore;
        _status = (int)IndexStatus.NotReady;
    }

    /// <summary>索引就绪状态 — 供 Agent 层判断查询路径。</summary>
    public IndexStatus Status => (IndexStatus)_status;

    /// <summary>当前已索引的块数量。无锁读取。</summary>
    public int ChunkCount => _metadata.Count;

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

        var texts = toEmbed
            .Select(c => c.SourceText ?? string.Empty)
            .ToList();
        float[][] vectors;
        try {
            vectors = await _embedModel.EmbedBatchAsync(texts, ct).ConfigureAwait(false);
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
                StartLine = chunk.StartLine,
                EndLine = chunk.EndLine,
                ParentChunkId = chunk.ParentChunkId,
                SourceText = chunk.SourceText
            }));
            hashesToAdd.Add((chunk.ChunkId, chunk.ContentHash));
            fileChunksToAdd.Add((chunk.FilePath, chunk.ChunkId));
            successCount++;
        }

        CasBulkUpdateMetadata(metadataToAdd);
        CasBulkUpdateHashes(hashesToAdd);
        CasBulkUpdateFileChunks(fileChunksToAdd);

        var newStatus = successCount == toEmbed.Count
            ? IndexStatus.Ready
            : successCount > 0
                ? IndexStatus.Partial
                : IndexStatus.Error;
        Interlocked.Exchange(ref _status, (int)newStatus);
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
            queryVector = await _embedModel.EmbedAsync(query, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            return [];
        }

        var annResults = _ann.Search(queryVector, topK, ct);
        if (annResults.Count == 0) return [];

        var metadataSnapshot = _metadata;
        var results = new List<ChunkSearchResult>(annResults.Count);
        var includeSource = options?.IncludeSourceText ?? false;
        var includeParent = options?.IncludeParentDocument ?? true;
        foreach (var (id, score) in annResults) {
            if (!metadataSnapshot.TryGetValue(id, out var meta)) continue;
            var result = new ChunkSearchResult {
                ChunkId = meta.ChunkId,
                FilePath = meta.FilePath,
                SymbolFqn = meta.SymbolFqn,
                StartLine = meta.StartLine,
                EndLine = meta.EndLine,
                Score = score,
                SourceText = includeSource ? meta.SourceText : null
            };
            results.Add(includeParent
                ? TryAttachParentDocument(result, meta.ParentChunkId)
                : result);
        }
        return results;
    }

    /// <summary>
    /// 尝试附加父文档原文到搜索结果 — 父文档检索召回时填充完整上下文。
    /// </summary>
    private ChunkSearchResult TryAttachParentDocument(ChunkSearchResult result, string? parentChunkId) {
        if (_parentStore is null || parentChunkId is null) return result;
        var parentDoc = _parentStore.Get(parentChunkId);
        if (parentDoc is null) return result;
        return result with {
            ParentDocumentText = parentDoc.SourceText,
            ParentStartLine = parentDoc.StartLine,
            ParentEndLine = parentDoc.EndLine,
            ParentSymbolFqn = parentDoc.SymbolFqn
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

    /// <summary>释放资源（无锁实现，空操作）。</summary>
    public void Dispose() {
        Interlocked.Exchange(ref _disposed, 1);
    }

    /// <summary>异步释放资源（无锁实现，空操作）。</summary>
    public ValueTask DisposeAsync() {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}
