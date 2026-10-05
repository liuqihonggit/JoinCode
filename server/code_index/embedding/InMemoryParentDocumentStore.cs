namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// 内存父文档存储 — 纯内存实现，无锁并发安全（ImmutableHamT + CAS）。
/// <para>父文档检索：向量库存小块，召回小块后通过 ParentChunkId 查此存储取父文档原文。</para>
/// <para>持久化通过 IKvStore（9LSM-Tree）存储，不再用 bin 文件。</para>
/// <para>读操作完全无锁 O(log₃₂ N) 查找；写操作 CAS 路径复制，读多写少场景最优。</para>
/// </summary>
public sealed class InMemoryParentDocumentStore : IParentDocumentStore, IIndexStore, IDisposable {

    private readonly IKvStore _kvStore;
    private volatile ImmutableHamT<string, ParentDocument> _documents = ImmutableHamT<string, ParentDocument>.Empty;
    private volatile ImmutableHamT<string, ImmutableHashSet<string>> _fileToDocs = ImmutableHamT<string, ImmutableHashSet<string>>.Empty;
    private int _disposed;
    private static readonly byte[] KeyParentDocs = System.Text.Encoding.UTF8.GetBytes("parent-docs");

    /// <summary>
    /// 构造内存父文档存储。
    /// </summary>
    /// <param name="kvStore">KV 存储抽象 — 用于持久化。</param>
    public InMemoryParentDocumentStore(IKvStore kvStore) {
        ArgumentNullException.ThrowIfNull(kvStore);
        _kvStore = kvStore;
    }

    /// <summary>索引类型标识。</summary>
    public IndexKind Kind => IndexKind.Parent;

    /// <summary>当前父文档数量。</summary>
    public int Count => _documents.Count;

    /// <summary>是否已就绪（有文档即就绪）。</summary>
    public bool IsReady => Count > 0;

    /// <summary>添加单个父文档。</summary>
    public void Add(ParentDocument document) {
        ArgumentNullException.ThrowIfNull(document);
        AddCore(document.ChunkId, document, document.FilePath);
    }

    /// <summary>批量添加父文档。</summary>
    public void AddRange(IReadOnlyList<ParentDocument> documents) {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) return;
        var spin = new SpinWait();
        while (true) {
            var current = _documents;
            var updated = current;
            foreach (var doc in documents)
                updated = updated.SetItem(doc.ChunkId, doc);
            if (Interlocked.CompareExchange(ref _documents, updated, current) == current) break;
            spin.SpinOnce();
        }
        while (true) {
            var current = _fileToDocs;
            var updated = current;
            foreach (var doc in documents) {
                var existing = updated.TryGetValue(doc.FilePath, out var set) ? set : ImmutableHashSet<string>.Empty;
                updated = updated.SetItem(doc.FilePath, existing.Add(doc.ChunkId));
            }
            if (Interlocked.CompareExchange(ref _fileToDocs, updated, current) == current) break;
            spin.SpinOnce();
        }
    }

    private void AddCore(string chunkId, ParentDocument doc, string filePath) {
        var spin = new SpinWait();
        while (true) {
            var current = _documents;
            var updated = current.SetItem(chunkId, doc);
            if (Interlocked.CompareExchange(ref _documents, updated, current) == current) break;
            spin.SpinOnce();
        }
        while (true) {
            var current = _fileToDocs;
            var existing = current.TryGetValue(filePath, out var set) ? set : ImmutableHashSet<string>.Empty;
            var updated = current.SetItem(filePath, existing.Add(chunkId));
            if (Interlocked.CompareExchange(ref _fileToDocs, updated, current) == current) break;
            spin.SpinOnce();
        }
    }

    /// <summary>按 ChunkId 获取父文档 — 不存在返回 null。无锁读取。</summary>
    public ParentDocument? Get(string chunkId) {
        ArgumentNullException.ThrowIfNull(chunkId);
        return _documents.TryGetValue(chunkId, out var doc) ? doc : null;
    }

    /// <summary>删除单个父文档。</summary>
    public void Remove(string chunkId) {
        ArgumentNullException.ThrowIfNull(chunkId);
        var doc = Get(chunkId);
        if (doc is null) return;
        var spin = new SpinWait();
        while (true) {
            var current = _documents;
            var updated = current.Remove(chunkId);
            if (Interlocked.CompareExchange(ref _documents, updated, current) == current) break;
            spin.SpinOnce();
        }
        while (true) {
            var current = _fileToDocs;
            if (!current.TryGetValue(doc.FilePath, out var docSet)) break;
            var newSet = docSet.Remove(chunkId);
            var updated = newSet.IsEmpty ? current.Remove(doc.FilePath) : current.SetItem(doc.FilePath, newSet);
            if (Interlocked.CompareExchange(ref _fileToDocs, updated, current) == current) break;
            spin.SpinOnce();
        }
    }

    /// <summary>删除文件关联的所有父文档。</summary>
    public void RemoveFile(string filePath) {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!_fileToDocs.TryGetValue(filePath, out var docIds)) return;
        var spin = new SpinWait();
        while (true) {
            var current = _documents;
            var updated = current.RemoveRange(docIds);
            if (Interlocked.CompareExchange(ref _documents, updated, current) == current) break;
            spin.SpinOnce();
        }
        while (true) {
            var current = _fileToDocs;
            var updated = current.Remove(filePath);
            if (Interlocked.CompareExchange(ref _fileToDocs, updated, current) == current) break;
            spin.SpinOnce();
        }
    }

    /// <summary>清空所有父文档。</summary>
    public void Clear() {
        _documents = ImmutableHamT<string, ParentDocument>.Empty;
        _fileToDocs = ImmutableHamT<string, ImmutableHashSet<string>>.Empty;
    }

    /// <summary>
    /// 持久化父文档到 IKvStore（LSM-Tree）。
    /// </summary>
    /// <param name="dirPath">目标目录路径（保留接口兼容，KV 存储自管理目录）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task SaveAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var snapshot = _documents;

        await using var ms = new MemoryStream();
        await using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8);
        bw.Write(System.Text.Encoding.UTF8.GetBytes("PRTDOC1"));
        bw.Write(snapshot.Count);
        foreach (var (_, doc) in snapshot) {
            ct.ThrowIfCancellationRequested();
            WriteString(bw, doc.ChunkId);
            WriteString(bw, doc.FilePath);
            WriteString(bw, doc.SymbolFqn);
            bw.Write(doc.StartLine);
            bw.Write(doc.EndLine);
            WriteString(bw, doc.SourceText);
            bw.Write(doc.IsTruncated);
        }
        bw.Flush();
        await _kvStore.PutAsync(KeyParentDocs, ms.ToArray(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 IKvStore 加载父文档。
    /// </summary>
    /// <param name="dirPath">源目录路径（保留接口兼容）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示加载成功；false 表示数据不存在或格式不匹配。</returns>
    public async Task<bool> LoadAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var bytes = await _kvStore.GetAsync(KeyParentDocs, ct).ConfigureAwait(false);
        if (bytes is null) return false;

        await using var ms = new MemoryStream(bytes, writable: false);
        using var br = new BinaryReader(ms, System.Text.Encoding.UTF8);
        var magic = System.Text.Encoding.UTF8.GetString(br.ReadBytes(7));
        if (magic != "PRTDOC1") return false;
        var count = br.ReadInt32();
        var docs = new List<ParentDocument>(count);
        for (var i = 0; i < count; i++) {
            ct.ThrowIfCancellationRequested();
            docs.Add(new ParentDocument {
                ChunkId = ReadString(br),
                FilePath = ReadString(br),
                SymbolFqn = ReadString(br),
                StartLine = br.ReadInt32(),
                EndLine = br.ReadInt32(),
                SourceText = ReadString(br),
                IsTruncated = br.ReadBoolean()
            });
        }
        AddRange(docs);
        return true;
    }

    /// <summary>
    /// 检查 IKvStore 中是否存在父文档持久化数据。
    /// </summary>
    public async Task<bool> ExistsAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var bytes = await _kvStore.GetAsync(KeyParentDocs, ct).ConfigureAwait(false);
        return bytes is not null;
    }

    private static void WriteString(BinaryWriter bw, string s) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static string ReadString(BinaryReader br) {
        var len = br.ReadInt32();
        return System.Text.Encoding.UTF8.GetString(br.ReadBytes(len));
    }

    /// <summary>释放资源（无锁实现，空操作）。</summary>
    public void Dispose() {
        Interlocked.Exchange(ref _disposed, 1);
    }
}
