namespace JoinCode.CodeIndex;

/// <summary>
/// 符号索引器 — 基于 InMemoryIndexStore 不可变快照 + CAS 无锁写入
/// 不再使用 SQLite 事务,所有写操作通过 CAS 原子完成
/// FTS5 全文检索替代为字符串包含匹配(后续可集成 SearchService 做 rg 模糊检索)
/// </summary>
public sealed class SymbolIndex : ISymbolIndex, IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly IFileSystem _fs;
    private readonly ILanguagePlugin _plugin;
    private readonly IClockService _clock;
    private int _disposed;

    /// <summary>
    /// 构造符号索引器 — 基于内存索引存储
    /// </summary>
    public SymbolIndex(InMemoryIndexStore store, IFileSystem fs, ILanguagePlugin plugin, IClockService? clock = null) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(plugin);
        _store = store;
        _fs = fs;
        _plugin = plugin;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 索引单个文件 — 读取文件内容并计算哈希后索引
    /// </summary>
    public async Task IndexFileAsync(string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!_fs.FileExists(filePath)) return;
        var (sourceCode, contentHash) = await HashUtility.ReadFileAndComputeHashAsync(filePath, _fs, ct).ConfigureAwait(false);
        await IndexFileWithContentAsync(filePath, sourceCode, contentHash, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 索引文件内容 — 先提取符号/调用/依赖,再写入索引
    /// </summary>
    public async Task IndexFileWithContentAsync(string filePath, string sourceCode, string contentHash, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(contentHash);
        var extraction = await _plugin.ExtractAllAsync(sourceCode, filePath, ct).ConfigureAwait(false);
        await IndexFileWithContentAsync(filePath, sourceCode, contentHash, extraction, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 索引文件内容 — 使用已提取的结果,CAS 原子完成 移除旧→插入新→更新追踪
    /// </summary>
    public Task IndexFileWithContentAsync(string filePath, string sourceCode, string contentHash, ExtractionResult extraction, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(extraction);
        var now = _clock.GetUtcNowOffset();
        _store.Update(snap => snap.IndexFile(filePath, contentHash, extraction, now));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 批量索引写入 — 单次 CAS 原子处理多个文件,仅一次 Inherits→Implements 修正
    /// </summary>
    public Task IndexFilesBatchAsync(
        IReadOnlyList<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)> files,
        CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0) return Task.CompletedTask;
        var now = _clock.GetUtcNowOffset();
        _store.Update(snap => snap.IndexFilesBatch(
            files.Select(f => (f.FilePath, f.Hash, f.Extraction)).ToList(), now));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 批量索引文件列表 — 逐个文件读取并索引
    /// </summary>
    public async Task IndexFilesAsync(IReadOnlyList<string> filePaths, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePaths);
        foreach (var fp in filePaths) {
            ct.ThrowIfCancellationRequested();
            await IndexFileAsync(fp, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 移除文件相关所有索引数据 — CAS 原子移除
    /// </summary>
    public Task RemoveFileAsync(string filePath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(filePath);
        _store.Update(snap => snap.RemoveFile(filePath));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清空所有索引数据
    /// </summary>
    public Task ClearAsync(CancellationToken ct) {
        _store.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取索引统计信息 — 无锁快照读取
    /// </summary>
    public Task<IndexStats> GetStatsAsync(CancellationToken ct) {
        var snap = _store.GetSnapshot();
        return Task.FromResult(new IndexStats {
            FileCount = snap.FileTracking.Count,
            SymbolCount = snap.SymbolsByFqn.Count,
            CallEdgeCount = snap.CallEdges.Count,
            DependencyEdgeCount = snap.DepEdges.Count,
            ProjectCount = snap.Projects.Count,
            LastUpdated = snap.LastUpdated
        });
    }

    /// <summary>
    /// 释放索引器资源
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
    }
}
