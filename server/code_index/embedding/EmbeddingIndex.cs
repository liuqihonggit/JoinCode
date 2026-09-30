namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// 向量索引 — 串联嵌入模型、向量存储、ANN 搜索。
/// <para>维护块哈希缓存：块没变就不重新嵌入（省 API 调用/计算）。</para>
/// <para>线程安全：读操作（Search）与写操作（Index/Remove）互斥。</para>
/// </summary>
public sealed class EmbeddingIndex : IAsyncDisposable {

    private readonly IEmbeddingModel _embedModel;
    private readonly IAnnSearch _ann;
    private readonly IParentDocumentStore? _parentStore;
    private readonly Dictionary<string, ChunkMetadata> _metadata = new();
    private readonly Dictionary<string, string> _chunkHashes = new();
    private readonly Dictionary<string, HashSet<string>> _fileToChunks = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private volatile int _status;
    private int _disposed;

    /// <summary>
    /// 构造向量索引。
    /// </summary>
    /// <param name="embedModel">嵌入模型（API/ONNX/simhash 均可）。</param>
    /// <param name="ann">ANN 搜索引擎（暴力/HNSW 均可）。</param>
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

    /// <summary>当前已索引的块数量。</summary>
    public int ChunkCount {
        get {
            _lock.EnterReadLock();
            try {
                return _metadata.Count;
            } finally {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// 批量索引代码块 — 跳过哈希未变的块。
    /// <para>首次索引后状态变为 Ready；部分失败则 Partial；全部失败则 Error。</para>
    /// </summary>
    public async Task IndexChunksAsync(IReadOnlyList<ChunkInfo> chunks, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0) return;

        var toEmbed = new List<ChunkInfo>();
        var unchanged = new List<ChunkInfo>();

        _lock.EnterReadLock();
        try {
            foreach (var chunk in chunks) {
                ct.ThrowIfCancellationRequested();
                if (_chunkHashes.TryGetValue(chunk.ChunkId, out var existingHash)
                    && existingHash == chunk.ContentHash) {
                    unchanged.Add(chunk);
                } else {
                    toEmbed.Add(chunk);
                }
            }
        } finally {
            _lock.ExitReadLock();
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
        _lock.EnterWriteLock();
        try {
            for (var i = 0; i < toEmbed.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var chunk = toEmbed[i];
                var vector = vectors[i];

                _ann.Add(chunk.ChunkId, vector);
                _metadata[chunk.ChunkId] = new ChunkMetadata {
                    ChunkId = chunk.ChunkId,
                    FilePath = chunk.FilePath,
                    SymbolFqn = chunk.SymbolFqn,
                    StartLine = chunk.StartLine,
                    EndLine = chunk.EndLine,
                    ParentChunkId = chunk.ParentChunkId
                };
                _chunkHashes[chunk.ChunkId] = chunk.ContentHash;

                if (!_fileToChunks.TryGetValue(chunk.FilePath, out var chunkSet)) {
                    chunkSet = [];
                    _fileToChunks[chunk.FilePath] = chunkSet;
                }
                chunkSet.Add(chunk.ChunkId);
                successCount++;
            }
        } finally {
            _lock.ExitWriteLock();
        }

        var newStatus = successCount == toEmbed.Count
            ? IndexStatus.Ready
            : successCount > 0
                ? IndexStatus.Partial
                : IndexStatus.Error;
        Interlocked.Exchange(ref _status, (int)newStatus);
    }

    /// <summary>
    /// 语义搜索 — 查询文本 → 嵌入 → ANN → 元数据。
    /// <para>索引未就绪时返回空列表（调用方可降级到符号搜索）。</para>
    /// </summary>
    public async Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        string query, int topK, CancellationToken ct) {
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

        var results = new List<ChunkSearchResult>(annResults.Count);
        _lock.EnterReadLock();
        try {
            foreach (var (id, score) in annResults) {
                if (!_metadata.TryGetValue(id, out var meta)) continue;
                var result = new ChunkSearchResult {
                    ChunkId = meta.ChunkId,
                    FilePath = meta.FilePath,
                    SymbolFqn = meta.SymbolFqn,
                    StartLine = meta.StartLine,
                    EndLine = meta.EndLine,
                    Score = score
                };
                results.Add(TryAttachParentDocument(result, meta.ParentChunkId));
            }
        } finally {
            _lock.ExitReadLock();
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
        _lock.EnterWriteLock();
        try {
            if (!_fileToChunks.TryGetValue(filePath, out var chunkIds)) {
                return Task.CompletedTask;
            }
            foreach (var chunkId in chunkIds) {
                _ann.Remove(chunkId);
                _metadata.Remove(chunkId);
                _chunkHashes.Remove(chunkId);
            }
            _fileToChunks.Remove(filePath);
        } finally {
            _lock.ExitWriteLock();
        }
        if (_metadata.Count == 0) {
            Interlocked.Exchange(ref _status, (int)IndexStatus.NotReady);
        }
        return Task.CompletedTask;
    }

    /// <summary>清空索引。</summary>
    public void Clear() {
        _lock.EnterWriteLock();
        try {
            _metadata.Clear();
            _chunkHashes.Clear();
            _fileToChunks.Clear();
        } finally {
            _lock.ExitWriteLock();
        }
        Interlocked.Exchange(ref _status, (int)IndexStatus.NotReady);
    }

    /// <summary>释放锁资源。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lock.Dispose();
    }

    /// <summary>异步释放锁资源。</summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
