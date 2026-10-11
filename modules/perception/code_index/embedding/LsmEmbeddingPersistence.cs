// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// LSM-Tree 持久化后端 — PithosDB 原生增量持久化。
/// <para>每个 chunk 的向量/元数据/哈希分别作为 KV 条目存储，支持增量写入和崩溃恢复。</para>
/// <para>Key 编码: v:{chunkId}=向量, m:{chunkId}=元数据, h:{chunkId}=哈希, g:graph=图, s:dims=维度。</para>
/// </summary>
internal sealed class LsmEmbeddingPersistence : IEmbeddingPersistence {
    private readonly IFileSystem _fs;
    private PithosKvStore? _incrementalStore;
    private string? _incrementalDir;

    private static byte[] KeyVector(string chunkId) => Encoding.UTF8.GetBytes($"v:{chunkId}");
    private static byte[] KeyMeta(string chunkId) => Encoding.UTF8.GetBytes($"m:{chunkId}");
    private static byte[] KeyHash(string chunkId) => Encoding.UTF8.GetBytes($"h:{chunkId}");
    private static readonly byte[] KeyGraph = Encoding.UTF8.GetBytes("g:graph");
    private static readonly byte[] KeyDims = Encoding.UTF8.GetBytes("s:dims");

    private static readonly byte[] PrefixVector = Encoding.UTF8.GetBytes("v:");
    private static readonly byte[] PrefixMeta = Encoding.UTF8.GetBytes("m:");
    private static readonly byte[] PrefixHash = Encoding.UTF8.GetBytes("h:");
    private static readonly byte[] PrefixVectorEnd = Encoding.UTF8.GetBytes("v;");
    private static readonly byte[] PrefixMetaEnd = Encoding.UTF8.GetBytes("m;");
    private static readonly byte[] PrefixHashEnd = Encoding.UTF8.GetBytes("h;");

    /// <summary>
    /// 构造 LSM 持久化后端。
    /// </summary>
    /// <param name="fs">文件系统抽象 — 用于目录检查。</param>
    public LsmEmbeddingPersistence(IFileSystem fs) {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
    }

    /// <summary>检查指定目录是否存在 kvstore/ 子目录。</summary>
    public Task<bool> ExistsAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var kvDir = Path.Combine(dirPath, "kvstore");
        return Task.FromResult(_fs.DirectoryExists(kvDir));
    }

    /// <summary>全量保存快照到 PithosDB LSM-Tree。</summary>
    public async Task SaveAsync(string dirPath, EmbeddingSnapshot snapshot, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var chunks = snapshot.Chunks;
        var dims = snapshot.Dims;

        var kvDir = Path.Combine(dirPath, "kvstore");
        if (_fs.DirectoryExists(kvDir)) {
            _fs.DeleteDirectory(kvDir, recursive: true);
        }
        _fs.CreateDirectory(kvDir);

        await using var store = new PithosKvStore(kvDir, new PithosOptions { DisableCompaction = true });
        await store.PutAsync(KeyDims, BitConverter.GetBytes(dims), ct).ConfigureAwait(false);

        foreach (var (chunkId, vector, meta, hash) in chunks) {
            ct.ThrowIfCancellationRequested();
            var vectorBytes = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
            await store.PutAsync(KeyVector(chunkId), vectorBytes, ct).ConfigureAwait(false);
            await store.PutAsync(KeyMeta(chunkId), SerializeMeta(meta), ct).ConfigureAwait(false);
            await store.PutAsync(KeyHash(chunkId), Encoding.UTF8.GetBytes(hash), ct).ConfigureAwait(false);
        }

        if (snapshot.GraphBytes is not null) {
            await store.PutAsync(KeyGraph, snapshot.GraphBytes, ct).ConfigureAwait(false);
        }
    }

    /// <summary>从 PithosDB LSM-Tree 加载快照。</summary>
    public async Task<EmbeddingSnapshot?> LoadAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var kvDir = Path.Combine(dirPath, "kvstore");
        if (!_fs.DirectoryExists(kvDir)) return null;

        await using var store = new PithosKvStore(kvDir, new PithosOptions { DisableCompaction = true });

        var dimsBytes = await store.GetAsync(KeyDims, ct).ConfigureAwait(false);
        if (dimsBytes is null) return null;
        var dims = BitConverter.ToInt32(dimsBytes);

        var vectors = new Dictionary<string, float[]>();
        var metas = new Dictionary<string, ChunkMetadata>();
        var hashes = new Dictionary<string, string>();

        await foreach (var (key, value) in store.ScanAsync(from: PrefixVector, to: PrefixVectorEnd, ct).ConfigureAwait(false)) {
            ct.ThrowIfCancellationRequested();
            var chunkId = Encoding.UTF8.GetString(key.AsSpan(2));
            var floats = new float[dims];
            var src = MemoryMarshal.Cast<byte, float>(value);
            src.CopyTo(floats);
            vectors[chunkId] = floats;
        }

        await foreach (var (key, value) in store.ScanAsync(from: PrefixMeta, to: PrefixMetaEnd, ct).ConfigureAwait(false)) {
            ct.ThrowIfCancellationRequested();
            var chunkId = Encoding.UTF8.GetString(key.AsSpan(2));
            metas[chunkId] = DeserializeMeta(value);
        }

        await foreach (var (key, value) in store.ScanAsync(from: PrefixHash, to: PrefixHashEnd, ct).ConfigureAwait(false)) {
            ct.ThrowIfCancellationRequested();
            var chunkId = Encoding.UTF8.GetString(key.AsSpan(2));
            hashes[chunkId] = Encoding.UTF8.GetString(value);
        }

        var graphBytes = await store.GetAsync(KeyGraph, ct).ConfigureAwait(false);

        var chunks = new List<(string, float[], ChunkMetadata, string)>(vectors.Count);
        foreach (var (chunkId, vec) in vectors) {
            if (!metas.TryGetValue(chunkId, out var meta)) continue;
            hashes.TryGetValue(chunkId, out var hash);
            chunks.Add((chunkId, vec, meta, hash ?? string.Empty));
        }

        return new EmbeddingSnapshot {
            Dims = dims,
            Chunks = chunks,
            GraphBytes = graphBytes
        };
    }

    /// <summary>序列化 ChunkMetadata 为 byte[]。</summary>
    private static byte[] SerializeMeta(ChunkMetadata meta) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8);
        bw.Write(meta.ChunkId);
        bw.Write(meta.FilePath);
        bw.Write(meta.SymbolFqn);
        bw.Write(meta.SymbolKind ?? string.Empty);
        bw.Write(meta.StartLine);
        bw.Write(meta.EndLine);
        bw.Write(meta.ParentFilePath is not null);
        if (meta.ParentFilePath is not null) {
            bw.Write(meta.ParentFilePath);
            bw.Write(meta.ParentStartLine);
            bw.Write(meta.ParentEndLine);
            bw.Write(meta.ParentSymbolFqn ?? string.Empty);
        }
        bw.Write(meta.SourceText is not null);
        if (meta.SourceText is not null) bw.Write(meta.SourceText);
        bw.Write(meta.SourceTextOffset);
        bw.Write(meta.SourceTextLen);
        bw.Write(meta.ContainedSymbolFqns.Count);
        foreach (var fqn in meta.ContainedSymbolFqns) {
            bw.Write(fqn);
        }
        bw.Write(meta.ContainedSymbolKinds);
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>反序列化 ChunkMetadata。</summary>
    private static ChunkMetadata DeserializeMeta(byte[] data) {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms, Encoding.UTF8);
        var chunkId = br.ReadString();
        var filePath = br.ReadString();
        var symbolFqn = br.ReadString();
        var symbolKind = br.ReadString();
        var startLine = br.ReadInt32();
        var endLine = br.ReadInt32();
        var hasParent = br.ReadBoolean();
        string? parentFilePath = null;
        var parentStartLine = 0;
        var parentEndLine = 0;
        string? parentSymbolFqn = null;
        if (hasParent) {
            parentFilePath = br.ReadString();
            parentStartLine = br.ReadInt32();
            parentEndLine = br.ReadInt32();
            var fqn = br.ReadString();
            parentSymbolFqn = fqn.Length == 0 ? null : fqn;
        }
        var hasSourceText = br.ReadBoolean();
        var sourceText = hasSourceText ? br.ReadString() : null;
        var sourceTextOffset = br.ReadInt64();
        var sourceTextLen = br.ReadInt32();
        var fqnCount = br.ReadInt32();
        var fqns = new string[fqnCount];
        for (var i = 0; i < fqnCount; i++) {
            fqns[i] = br.ReadString();
        }
        var containedSymbolKinds = br.ReadInt32();
        return new ChunkMetadata {
            ChunkId = chunkId,
            FilePath = filePath,
            SymbolFqn = symbolFqn,
            SymbolKind = symbolKind,
            StartLine = startLine,
            EndLine = endLine,
            ParentFilePath = parentFilePath,
            ParentStartLine = parentStartLine,
            ParentEndLine = parentEndLine,
            ParentSymbolFqn = parentSymbolFqn,
            SourceText = sourceText,
            SourceTextOffset = sourceTextOffset,
            SourceTextLen = sourceTextLen,
            ContainedSymbolFqns = fqns,
            ContainedSymbolKinds = containedSymbolKinds,
        };
    }

    /// <summary>增量写入单个 chunk — 自动打开/复用 LSM 存储，无需显式打开。</summary>
    public async Task PersistChunkAsync(string dirPath, string chunkId, float[] vector, ChunkMetadata meta, string hash, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        ArgumentNullException.ThrowIfNull(chunkId);
        await EnsureStoreOpenAsync(dirPath, ct).ConfigureAwait(false);
        var store = _incrementalStore!;
        var vectorBytes = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
        await store.PutAsync(KeyVector(chunkId), vectorBytes, ct).ConfigureAwait(false);
        await store.PutAsync(KeyMeta(chunkId), SerializeMeta(meta), ct).ConfigureAwait(false);
        await store.PutAsync(KeyHash(chunkId), Encoding.UTF8.GetBytes(hash), ct).ConfigureAwait(false);
        await store.PutAsync(KeyDims, BitConverter.GetBytes(vector.Length), ct).ConfigureAwait(false);
    }

    /// <summary>增量删除单个 chunk — 自动打开/复用 LSM 存储，写入墓碑标记。</summary>
    public async Task DeleteChunkAsync(string dirPath, string chunkId, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        ArgumentNullException.ThrowIfNull(chunkId);
        await EnsureStoreOpenAsync(dirPath, ct).ConfigureAwait(false);
        var store = _incrementalStore!;
        await store.DeleteAsync(KeyVector(chunkId), ct).ConfigureAwait(false);
        await store.DeleteAsync(KeyMeta(chunkId), ct).ConfigureAwait(false);
        await store.DeleteAsync(KeyHash(chunkId), ct).ConfigureAwait(false);
    }

    /// <summary>刷盘 — LSM 每次 PutAsync 已持久化，空操作。</summary>
    public Task FlushAsync(string dirPath, CancellationToken ct) => Task.CompletedTask;

    /// <summary>确保 LSM 存储已打开且对应正确目录 — 目录不匹配时自动切换。</summary>
    private async Task EnsureStoreOpenAsync(string dirPath, CancellationToken ct) {
        var kvDir = Path.Combine(dirPath, "kvstore");
        if (_incrementalStore is not null && _incrementalDir == kvDir) return;
        if (_incrementalStore is not null) {
            await _incrementalStore.DisposeAsync().ConfigureAwait(false);
        }
        _fs.CreateDirectory(kvDir);
        _incrementalDir = kvDir;
        _incrementalStore = new PithosKvStore(kvDir);
    }

    /// <summary>释放资源 — 关闭增量存储。</summary>
    public async ValueTask DisposeAsync() {
        if (_incrementalStore is not null) {
            await _incrementalStore.DisposeAsync().ConfigureAwait(false);
            _incrementalStore = null;
        }
    }
}
