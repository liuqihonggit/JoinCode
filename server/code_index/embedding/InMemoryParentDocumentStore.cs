namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// 内存父文档存储 — 纯内存实现，线程安全（ReaderWriterLockSlim）。
/// <para>父文档检索：向量库存小块，召回小块后通过 ParentChunkId 查此存储取父文档原文。</para>
/// <para>进程退出释放，下次重建（纯内存无持久化）。</para>
/// </summary>
public sealed class InMemoryParentDocumentStore : IParentDocumentStore, IDisposable {

    private readonly Dictionary<string, ParentDocument> _documents = new();
    private readonly Dictionary<string, HashSet<string>> _fileToDocs = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private int _disposed;

    /// <summary>当前父文档数量。</summary>
    public int Count {
        get {
            _lock.EnterReadLock();
            try {
                return _documents.Count;
            } finally {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>添加单个父文档。</summary>
    public void Add(ParentDocument document) {
        ArgumentNullException.ThrowIfNull(document);
        _lock.EnterWriteLock();
        try {
            _documents[document.ChunkId] = document;
            if (!_fileToDocs.TryGetValue(document.FilePath, out var docSet)) {
                docSet = [];
                _fileToDocs[document.FilePath] = docSet;
            }
            docSet.Add(document.ChunkId);
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>批量添加父文档。</summary>
    public void AddRange(IReadOnlyList<ParentDocument> documents) {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) return;
        _lock.EnterWriteLock();
        try {
            foreach (var doc in documents) {
                _documents[doc.ChunkId] = doc;
                if (!_fileToDocs.TryGetValue(doc.FilePath, out var docSet)) {
                    docSet = [];
                    _fileToDocs[doc.FilePath] = docSet;
                }
                docSet.Add(doc.ChunkId);
            }
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>按 ChunkId 获取父文档 — 不存在返回 null。</summary>
    public ParentDocument? Get(string chunkId) {
        ArgumentNullException.ThrowIfNull(chunkId);
        _lock.EnterReadLock();
        try {
            return _documents.TryGetValue(chunkId, out var doc) ? doc : null;
        } finally {
            _lock.ExitReadLock();
        }
    }

    /// <summary>删除单个父文档。</summary>
    public void Remove(string chunkId) {
        ArgumentNullException.ThrowIfNull(chunkId);
        _lock.EnterWriteLock();
        try {
            if (!_documents.TryGetValue(chunkId, out var doc)) return;
            _documents.Remove(chunkId);
            if (_fileToDocs.TryGetValue(doc.FilePath, out var docSet)) {
                docSet.Remove(chunkId);
                if (docSet.Count == 0) _fileToDocs.Remove(doc.FilePath);
            }
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>删除文件关联的所有父文档。</summary>
    public void RemoveFile(string filePath) {
        ArgumentNullException.ThrowIfNull(filePath);
        _lock.EnterWriteLock();
        try {
            if (!_fileToDocs.TryGetValue(filePath, out var docIds)) return;
            foreach (var chunkId in docIds) {
                _documents.Remove(chunkId);
            }
            _fileToDocs.Remove(filePath);
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>清空所有父文档。</summary>
    public void Clear() {
        _lock.EnterWriteLock();
        try {
            _documents.Clear();
            _fileToDocs.Clear();
        } finally {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>释放锁资源。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lock.Dispose();
    }
}
