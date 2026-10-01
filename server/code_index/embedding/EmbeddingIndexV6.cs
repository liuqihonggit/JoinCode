namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// VECIDX6 — PithosDB LSM-Tree 持久化后端。
/// <para>每个 chunk 的向量/元数据/哈希分别作为 KV 条目存储,支持增量写入和崩溃恢复。</para>
/// <para>Key 编码: v:{chunkId}=向量, m:{chunkId}=元数据, h:{chunkId}=哈希, g:graph=图, s:dims=维度。</para>
/// <para>相比 VECIDX4(mmap 批量序列化) 和 VECIDX5(分页+跳表), V6 用 LSM-Tree 实现真正的增量持久化。</para>
/// </summary>
public sealed partial class EmbeddingIndex {

    private PithosKvStore? _kvStoreV6;
    private string? _kvDirV6;

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
    /// VECIDX6 持久化 — 将所有块写入 PithosDB LSM-Tree。
    /// </summary>
    /// <param name="dirPath">目标目录路径(kvstore 子目录存储 KV 数据)。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task SaveAsyncV6(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var chunks = CollectChunksForSave();
        var dims = chunks.Count > 0 ? chunks[0].Vector.Length : 0;

        var kvDir = Path.Combine(dirPath, "kvstore");
        _fs.CreateDirectory(kvDir);

        await using var store = new PithosKvStore(kvDir);
        await store.PutAsync(KeyDims, BitConverter.GetBytes(dims), ct).ConfigureAwait(false);

        foreach (var (chunkId, vector, meta, hash) in chunks) {
            ct.ThrowIfCancellationRequested();
            var vectorBytes = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
            await store.PutAsync(KeyVector(chunkId), vectorBytes, ct).ConfigureAwait(false);
            await store.PutAsync(KeyMeta(chunkId), SerializeMeta(meta), ct).ConfigureAwait(false);
            await store.PutAsync(KeyHash(chunkId), Encoding.UTF8.GetBytes(hash), ct).ConfigureAwait(false);
        }

        var graphBytes = await BuildGraphRegionV6Async().ConfigureAwait(false);
        if (graphBytes is not null) {
            await store.PutAsync(KeyGraph, graphBytes, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// VECIDX6 加载 — 从 PithosDB LSM-Tree 恢复所有块。
    /// </summary>
    /// <param name="dirPath">源目录路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示加载成功。</returns>
    public async Task<bool> LoadAsyncV6(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var kvDir = Path.Combine(dirPath, "kvstore");
        if (!_fs.DirectoryExists(kvDir)) return false;

        await using var store = new PithosKvStore(kvDir);

        var dimsBytes = await store.GetAsync(KeyDims, ct).ConfigureAwait(false);
        if (dimsBytes is null) return false;
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
        if (graphBytes is not null) {
            LoadGraphV6(graphBytes, vectors);
        }

        var metadataBuilder = ImmutableHamT.CreateBuilder<string, ChunkMetadata>();
        var hashBuilder = ImmutableHamT.CreateBuilder<string, string>();
        var vectorBuilder = ImmutableHamT.CreateBuilder<string, float[]>();
        var fileToChunksBuilder = ImmutableHamT.CreateBuilder<string, ImmutableHashSet<string>>();

        foreach (var (chunkId, vec) in vectors) {
            vectorBuilder.Add(chunkId, vec);
            if (metas.TryGetValue(chunkId, out var meta)) {
                metadataBuilder.Add(chunkId, meta);
                fileToChunksBuilder.TryGetValue(meta.FilePath, out var existing);
                fileToChunksBuilder[meta.FilePath] = (existing ?? ImmutableHashSet<string>.Empty).Add(chunkId);
            }
            if (hashes.TryGetValue(chunkId, out var hash)) {
                hashBuilder.Add(chunkId, hash);
            }
        }

        Interlocked.Exchange(ref _metadata, metadataBuilder.ToImmutable());
        Interlocked.Exchange(ref _chunkHashes, hashBuilder.ToImmutable());
        Interlocked.Exchange(ref _vectors, vectorBuilder.ToImmutable());
        Interlocked.Exchange(ref _fileToChunks, fileToChunksBuilder.ToImmutable());
        Interlocked.Exchange(ref _status, (int)IndexStatus.Ready);
        _indexFilePath = Path.Combine(dirPath, "vector_index.bin");

        return true;
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
        bw.Write(meta.ParentChunkId is not null);
        if (meta.ParentChunkId is not null) bw.Write(meta.ParentChunkId);
        bw.Write(meta.SourceText is not null);
        if (meta.SourceText is not null) bw.Write(meta.SourceText);
        bw.Write(meta.SourceTextOffset);
        bw.Write(meta.SourceTextLen);
        bw.Write(meta.ContainedSymbolFqns.Count);
        foreach (var fqn in meta.ContainedSymbolFqns) {
            bw.Write(fqn);
        }
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
        var parentChunkId = hasParent ? br.ReadString() : null;
        var hasSourceText = br.ReadBoolean();
        var sourceText = hasSourceText ? br.ReadString() : null;
        var sourceTextOffset = br.ReadInt64();
        var sourceTextLen = br.ReadInt32();
        var fqnCount = br.ReadInt32();
        var fqns = new string[fqnCount];
        for (var i = 0; i < fqnCount; i++) {
            fqns[i] = br.ReadString();
        }
        return new ChunkMetadata {
            ChunkId = chunkId,
            FilePath = filePath,
            SymbolFqn = symbolFqn,
            SymbolKind = symbolKind,
            StartLine = startLine,
            EndLine = endLine,
            ParentChunkId = parentChunkId,
            SourceText = sourceText,
            SourceTextOffset = sourceTextOffset,
            SourceTextLen = sourceTextLen,
            ContainedSymbolFqns = fqns,
        };
    }

    /// <summary>构建图段字节 — 委托给 IAnnSearchGraphPersistence。</summary>
    private async Task<byte[]?> BuildGraphRegionV6Async() {
        if (_ann is not IAnnSearchGraphPersistence graphPersist) return null;
        await using var ms = new MemoryStream();
        await using var bw = new BinaryWriter(ms, Encoding.UTF8);
        bw.Write((byte)1);
        graphPersist.SaveGraph(bw);
        bw.Flush();
        await Task.CompletedTask.ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>从图段字节加载图。</summary>
    private void LoadGraphV6(byte[] graphBytes, Dictionary<string, float[]> vectors) {
        if (_ann is not IAnnSearchGraphPersistence gp) {
            foreach (var (id, vec) in vectors) {
                _ann.Add(id, vec);
            }
            return;
        }
        using var ms = new MemoryStream(graphBytes);
        using var br = new BinaryReader(ms, Encoding.UTF8);
        var hasGraph = br.ReadByte();
        if (hasGraph == 1) {
            gp.LoadGraph(br, vectors);
        } else {
            foreach (var (id, vec) in vectors) {
                _ann.Add(id, vec);
            }
        }
    }

    /// <summary>
    /// 打开 V6 KV 存储 — 后续增量写入/删除直接操作此实例,无需全量 SaveAsyncV6。
    /// </summary>
    /// <param name="dirPath">数据目录路径(kvstore 子目录存储 KV 数据)。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task OpenKvStoreV6(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var kvDir = Path.Combine(dirPath, "kvstore");
        _fs.CreateDirectory(kvDir);
        _kvDirV6 = kvDir;
        _kvStoreV6 = new PithosKvStore(kvDir);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 增量写入单个 chunk — 直接 PutAsync 到 KV 存储,无需全量重建索引。
    /// <para>需先调用 OpenKvStoreV6 打开 KV 存储。</para>
    /// </summary>
    internal async Task PersistChunkAsyncV6(
        string chunkId, float[] vector, ChunkMetadata meta, string hash,
        CancellationToken ct = default) {
        await EnsureKvStoreV6().ConfigureAwait(false);
        var store = _kvStoreV6!;
        var vectorBytes = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
        await store.PutAsync(KeyVector(chunkId), vectorBytes, ct).ConfigureAwait(false);
        await store.PutAsync(KeyMeta(chunkId), SerializeMeta(meta), ct).ConfigureAwait(false);
        await store.PutAsync(KeyHash(chunkId), Encoding.UTF8.GetBytes(hash), ct).ConfigureAwait(false);
        var dims = vector.Length;
        await store.PutAsync(KeyDims, BitConverter.GetBytes(dims), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 增量删除单个 chunk — 写入墓碑标记,压实时物理删除。
    /// <para>需先调用 OpenKvStoreV6 打开 KV 存储。</para>
    /// </summary>
    public async Task DeleteChunkAsyncV6(string chunkId, CancellationToken ct = default) {
        await EnsureKvStoreV6().ConfigureAwait(false);
        var store = _kvStoreV6!;
        await store.DeleteAsync(KeyVector(chunkId), ct).ConfigureAwait(false);
        await store.DeleteAsync(KeyMeta(chunkId), ct).ConfigureAwait(false);
        await store.DeleteAsync(KeyHash(chunkId), ct).ConfigureAwait(false);
    }

    /// <summary>关闭 V6 KV 存储 — 在 Dispose 中调用。</summary>
    private async Task CloseKvStoreV6Async() {
        if (_kvStoreV6 is not null) {
            await _kvStoreV6.DisposeAsync().ConfigureAwait(false);
            _kvStoreV6 = null;
        }
    }

    /// <summary>确保 KV 存储已打开。</summary>
    private async Task EnsureKvStoreV6() {
        if (_kvStoreV6 is null) {
            throw new InvalidOperationException("V6 KV 存储未打开,请先调用 OpenKvStoreV6");
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
