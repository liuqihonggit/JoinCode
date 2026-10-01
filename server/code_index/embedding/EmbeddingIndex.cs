namespace JoinCode.CodeIndex.Embedding;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly struct VectorIndexHeader {
    /// <summary>块数量。</summary>
    public readonly int Count;
    /// <summary>向量维度。</summary>
    public readonly int Dims;
    /// <summary>元数据段偏移量。</summary>
    public readonly long MetaOffset;
    /// <summary>SourceText 段偏移量。</summary>
    public readonly long SourceOffset;
    /// <summary>图段偏移量（0=无图）。</summary>
    public readonly long GraphOffset;

    /// <summary>构造文件头。</summary>
    public VectorIndexHeader(int count, int dims, long metaOffset, long sourceOffset, long graphOffset) {
        Count = count;
        Dims = dims;
        MetaOffset = metaOffset;
        SourceOffset = sourceOffset;
        GraphOffset = graphOffset;
    }
}

/// <summary>
/// 元数据固定段条目 — 8字节对齐，MemoryMarshal.Cast 直接映射。
/// <para>定长字段直接存储，变长字符串用 StringOffset/StringLen 指向字符串区。</para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly struct MetaEntryFixed {
    public readonly int StartLine;
    public readonly int EndLine;
    public readonly int SourceTextLen;
    public readonly int ContainedFqnCount;
    public readonly int ChunkIdLen;
    public readonly int FilePathLen;
    public readonly int FqnLen;
    public readonly int SymbolKindLen;
    public readonly int ParentChunkIdLen;
    public readonly int HashLen;
    public readonly int ContainedFqnsTotalLen;
    public readonly int _padding;
    public readonly long SourceTextOffset;
    public readonly long ChunkIdOffset;
    public readonly long FilePathOffset;
    public readonly long FqnOffset;
    public readonly long SymbolKindOffset;
    public readonly long ParentChunkIdOffset;
    public readonly long HashOffset;
    public readonly long ContainedFqnsOffset;

    /// <summary>构造元数据固定段条目。</summary>
    public MetaEntryFixed(
        int startLine, int endLine, int sourceTextLen, int containedFqnCount,
        int chunkIdLen, int filePathLen, int fqnLen, int symbolKindLen,
        int parentChunkIdLen, int hashLen, int containedFqnsTotalLen,
        long sourceTextOffset, long chunkIdOffset, long filePathOffset,
        long fqnOffset, long symbolKindOffset, long parentChunkIdOffset,
        long hashOffset, long containedFqnsOffset) {
        StartLine = startLine;
        EndLine = endLine;
        SourceTextLen = sourceTextLen;
        ContainedFqnCount = containedFqnCount;
        ChunkIdLen = chunkIdLen;
        FilePathLen = filePathLen;
        FqnLen = fqnLen;
        SymbolKindLen = symbolKindLen;
        ParentChunkIdLen = parentChunkIdLen;
        HashLen = hashLen;
        ContainedFqnsTotalLen = containedFqnsTotalLen;
        _padding = 0;
        SourceTextOffset = sourceTextOffset;
        ChunkIdOffset = chunkIdOffset;
        FilePathOffset = filePathOffset;
        FqnOffset = fqnOffset;
        SymbolKindOffset = symbolKindOffset;
        ParentChunkIdOffset = parentChunkIdOffset;
        HashOffset = hashOffset;
        ContainedFqnsOffset = containedFqnsOffset;
    }
}

/// <summary>
/// 向量索引 — 串联嵌入模型、向量存储、ANN 搜索。
/// <para>维护块哈希缓存：块没变就不重新嵌入（省 API 调用/计算）。</para>
/// <para>无锁并发安全：读操作（Search）完全无锁；写操作（Index/Remove）CAS 路径复制。</para>
/// <para>_ann 自身线程安全（BruteForceAnn 内部有锁），EmbeddingIndex 不再加全局锁。</para>
/// </summary>
public sealed class EmbeddingIndex : IAsyncDisposable, IIndexStore {

    private readonly IEmbeddingModel _embedModel;
    private readonly IAnnSearch _ann;
    private readonly IParentDocumentStore? _parentStore;
    private readonly IFileSystem _fs;
    private volatile ImmutableHamT<string, ChunkMetadata> _metadata = ImmutableHamT<string, ChunkMetadata>.Empty;
    private volatile ImmutableHamT<string, string> _chunkHashes = ImmutableHamT<string, string>.Empty;
    private volatile ImmutableHamT<string, ImmutableHashSet<string>> _fileToChunks = ImmutableHamT<string, ImmutableHashSet<string>>.Empty;
    private volatile ImmutableHamT<string, float[]> _vectors = ImmutableHamT<string, float[]>.Empty;
    private volatile int _status;
    private int _disposed;
    private string? _indexFilePath;

    /// <summary>
    /// 构造向量索引。
    /// </summary>
    /// <param name="embedModel">嵌入模型（API/ONNX/simhash 均可）。</param>
    /// <param name="ann">ANN 搜索引擎（暴力/HNSW 均可，需自身线程安全）。</param>
    /// <param name="fs">文件系统抽象 — 用于持久化读写。</param>
    /// <param name="parentStore">父文档存储（可选）— 注入后 SearchAsync 返回结果携带父文档原文。</param>
    public EmbeddingIndex(IEmbeddingModel embedModel, IAnnSearch ann, IFileSystem fs, IParentDocumentStore? parentStore = null) {
        ArgumentNullException.ThrowIfNull(embedModel);
        ArgumentNullException.ThrowIfNull(ann);
        ArgumentNullException.ThrowIfNull(fs);
        _embedModel = embedModel;
        _ann = ann;
        _fs = fs;
        _parentStore = parentStore;
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

        const int EmbedBatchSize = 128;
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
                ParentChunkId = chunk.ParentChunkId,
                SourceText = chunk.SourceText,
                ContainedSymbolFqns = chunk.ContainedSymbolFqns
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
    /// 持久化向量索引到目录 — VECIDX4 分页格式，结构体 Pack=8 对齐。
    /// <para>格式：Magic(8B) + Header(32B) + 向量段 + MetaEntryFixed[] + 字符串区 + SourceText段 + 图段。</para>
    /// <para>加载时 MemoryMarshal.Cast 零拷贝映射向量段和 MetaEntryFixed[]，跳过 SourceText 段。</para>
    /// </summary>
    /// <param name="dirPath">目标目录路径。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task SaveAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var chunks = CollectChunksForSave();
        var count = chunks.Count;
        var dims = count > 0 ? chunks[0].Vector.Length : 0;

        await using var stringRegion = new MemoryStream();
        await using var sourceRegion = new MemoryStream();
        var entries = new MetaEntryFixed[count];
        for (var i = 0; i < count; i++) {
            ct.ThrowIfCancellationRequested();
            entries[i] = BuildMetaEntry(chunks[i], stringRegion, sourceRegion);
        }

        var graphRegion = await BuildGraphRegionAsync().ConfigureAwait(false);
        var layout = ComputeIndexLayout(count, dims, stringRegion.Length, sourceRegion.Length, graphRegion.Length);

        _fs.CreateDirectory(dirPath);
        var filePath = Path.Combine(dirPath, "vector_index.bin");
        await WriteIndexFileAsync(filePath, layout, chunks, dims, entries, stringRegion, sourceRegion, graphRegion, ct).ConfigureAwait(false);
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

    /// <summary>构建单个元数据条目 — 写字符串区+SourceText段，返回 MetaEntryFixed 结构体。</summary>
    private static MetaEntryFixed BuildMetaEntry(
        (string ChunkId, float[] Vector, ChunkMetadata Meta, string Hash) chunk,
        MemoryStream stringRegion, MemoryStream sourceRegion) {
        var (chunkId, _, meta, hash) = chunk;

        WriteStringRegion(stringRegion, chunkId, out var chunkIdOff, out var chunkIdLen);
        WriteStringRegion(stringRegion, meta.FilePath, out var filePathOff, out var filePathLen);
        WriteStringRegion(stringRegion, meta.SymbolFqn, out var fqnOff, out var fqnLen);
        WriteStringRegion(stringRegion, meta.SymbolKind, out var symbolKindOff, out var symbolKindLen);
        WriteNullableStringRegion(stringRegion, meta.ParentChunkId, out var parentChunkIdOff, out var parentChunkIdLen);
        WriteStringRegion(stringRegion, hash, out var hashOff, out var hashLen);
        WriteContainedFqnsRegion(stringRegion, meta.ContainedSymbolFqns, out var containedFqnsOff, out var containedFqnsTotalLen);
        WriteSourceTextRegion(sourceRegion, meta.SourceText, out var sourceTextOff, out var sourceTextLen);

        return new MetaEntryFixed(
            meta.StartLine, meta.EndLine, sourceTextLen, meta.ContainedSymbolFqns.Count,
            chunkIdLen, filePathLen, fqnLen, symbolKindLen,
            parentChunkIdLen, hashLen, containedFqnsTotalLen,
            sourceTextOff, chunkIdOff, filePathOff, fqnOff, symbolKindOff,
            parentChunkIdOff, hashOff, containedFqnsOff);
    }

    /// <summary>写字符串到区，记录偏移和长度。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStringRegion(MemoryStream ms, string s, out long offset, out int len) {
        offset = ms.Position;
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        ms.Write(bytes);
        len = bytes.Length;
    }

    /// <summary>写可空字符串到区 — null 记 offset=-1, len=0。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteNullableStringRegion(MemoryStream ms, string? s, out long offset, out int len) {
        if (s is null) { offset = -1; len = 0; return; }
        WriteStringRegion(ms, s, out offset, out len);
    }

    /// <summary>写 ContainedFqns 到字符串区 — 每条前缀 4 字节长度（小端）+ UTF8 内容。</summary>
    private static void WriteContainedFqnsRegion(MemoryStream ms, IReadOnlyList<string> fqns, out long offset, out int totalLen) {
        offset = ms.Position;
        totalLen = 0;
        Span<byte> intBuf = stackalloc byte[4];
        foreach (var fqn in fqns) {
            var bytes = System.Text.Encoding.UTF8.GetBytes(fqn);
            BinaryPrimitives.WriteInt32LittleEndian(intBuf, bytes.Length);
            ms.Write(intBuf);
            ms.Write(bytes);
            totalLen += bytes.Length + 4;
        }
    }

    /// <summary>写 SourceText 到段 — null 记 offset=-1, len=0。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSourceTextRegion(MemoryStream ms, string? sourceText, out long offset, out int len) {
        if (sourceText is null) { offset = -1; len = 0; return; }
        offset = ms.Position;
        var bytes = System.Text.Encoding.UTF8.GetBytes(sourceText);
        ms.Write(bytes);
        len = bytes.Length;
    }

    /// <summary>构建图段 — HNSW 图持久化或空标记。</summary>
    private async Task<byte[]> BuildGraphRegionAsync() {
        await using var ms = new MemoryStream();
        await using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8);
        if (_ann is IAnnSearchGraphPersistence graphPersist) {
            bw.Write((byte)1);
            graphPersist.SaveGraph(bw);
        } else {
            bw.Write((byte)0);
        }
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>索引文件布局 — 各段偏移量，Pack=8 对齐。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private readonly struct IndexLayout {
        /// <summary>块数量。</summary>
        public readonly int Count;
        /// <summary>向量维度。</summary>
        public readonly int Dims;
        /// <summary>头总大小（Magic+Header）。</summary>
        public readonly long HeaderSize;
        /// <summary>元数据段偏移量。</summary>
        public readonly long MetaOffset;
        /// <summary>字符串区偏移量。</summary>
        public readonly long StringOffset;
        /// <summary>SourceText 段偏移量。</summary>
        public readonly long SourceOffset;
        /// <summary>图段偏移量。</summary>
        public readonly long GraphOffset;
        /// <summary>构造索引布局。</summary>
        public IndexLayout(int count, int dims, long headerSize, long metaOffset, long stringOffset, long sourceOffset, long graphOffset) {
            Count = count; Dims = dims; HeaderSize = headerSize;
            MetaOffset = metaOffset; StringOffset = stringOffset;
            SourceOffset = sourceOffset; GraphOffset = graphOffset;
        }
    }

    /// <summary>计算索引文件各段偏移量。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IndexLayout ComputeIndexLayout(int count, int dims, long stringLen, long sourceLen, long graphLen) {
        const int MagicSize = 8;
        var headerSize = MagicSize + Unsafe.SizeOf<VectorIndexHeader>();
        var vectorSize = (long)count * dims * sizeof(float);
        var metaSize = (long)count * Unsafe.SizeOf<MetaEntryFixed>();
        var metaOffset = headerSize + vectorSize;
        var stringOffset = metaOffset + metaSize;
        var sourceOffset = stringOffset + stringLen;
        var graphOffset = sourceOffset + sourceLen;
        return new IndexLayout(count, dims, headerSize, metaOffset, stringOffset, sourceOffset, graphOffset);
    }

    /// <summary>写入索引文件 — Magic+Header+向量段+Meta段+字符串区+SourceText段+图段。</summary>
    private async Task WriteIndexFileAsync(
        string filePath, IndexLayout layout,
        List<(string ChunkId, float[] Vector, ChunkMetadata Meta, string Hash)> chunks,
        int dims, MetaEntryFixed[] entries,
        MemoryStream stringRegion, MemoryStream sourceRegion, byte[] graphRegion,
        CancellationToken ct) {
        await using var ms = new MemoryStream();
        await using var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8);
        bw.Write(System.Text.Encoding.UTF8.GetBytes("VECIDX4\0"));
        var header = new VectorIndexHeader(layout.Count, layout.Dims, layout.MetaOffset, layout.SourceOffset, layout.GraphOffset);
        WriteStruct(bw, header);
        foreach (var (_, vector, _, _) in chunks) {
            bw.Write(MemoryMarshal.AsBytes(vector.AsSpan(0, dims)));
        }
        bw.Write(MemoryMarshal.AsBytes(entries.AsSpan()));
        bw.Write(stringRegion.GetBuffer(), 0, (int)stringRegion.Length);
        bw.Write(sourceRegion.GetBuffer(), 0, (int)sourceRegion.Length);
        bw.Write(graphRegion);
        bw.Flush();
        await _fs.WriteAllBytesAsync(filePath, ms.ToArray(), ct).ConfigureAwait(false);
    }

    /// <summary>泛型写结构体到 BinaryWriter — MemoryMarshal.AsBytes 零分配。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStruct<T>(BinaryWriter bw, T value) where T : struct {
        bw.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    }

    /// <summary>
    /// 从目录加载向量索引 — VECIDX4 分页格式，ReadExactlyAsync + span 零拷贝。
    /// <para>读取 Header+向量段+Meta段+字符串区（跳过 SourceText 段），MemoryMarshal.Cast 映射。</para>
    /// <para>字符串区直接用 span 引用 loadData，不 ToArray 零拷贝。</para>
    /// </summary>
    /// <param name="dirPath">源目录路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示加载成功；false 表示文件不存在或格式不匹配。</returns>
    public async Task<bool> LoadAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var filePath = Path.Combine(dirPath, "vector_index.bin");
        if (!_fs.FileExists(filePath)) return false;
        _indexFilePath = filePath;

        await using var fs = _fs.OpenRead(filePath);
        var header = await ReadIndexHeaderAsync(fs, ct).ConfigureAwait(false);
        if (header.Count == 0) return false;

        var loadData = await ReadLoadDataAsync(fs, header, ct).ConfigureAwait(false);
        var (metadataItems, hashItems, vectorItems, fileChunksItems, vectorsDict) = ParseMetadataEntriesFromSpan(header, loadData, ct);

        await LoadGraphSegmentAsync(fs, header, vectorsDict, ct).ConfigureAwait(false);

        CasBulkUpdateMetadata(metadataItems);
        CasBulkUpdateHashes(hashItems);
        CasBulkUpdateVectors(vectorItems);
        CasBulkUpdateFileChunks(fileChunksItems);
        Interlocked.Exchange(ref _status, (int)IndexStatus.Ready);
        return true;
    }

    /// <summary>读索引头 — Magic(8B) + VectorIndexHeader(32B)，校验 VECIDX4 魔数。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async Task<VectorIndexHeader> ReadIndexHeaderAsync(Stream fs, CancellationToken ct) {
        const int MagicSize = 8;
        var headerSize = MagicSize + Unsafe.SizeOf<VectorIndexHeader>();
        var headerBytes = new byte[headerSize];
        await fs.ReadExactlyAsync(headerBytes, ct).ConfigureAwait(false);
        var magic = Encoding.UTF8.GetString(headerBytes, 0, 7);
        if (magic != "VECIDX4") return default;
        return MemoryMarshal.Read<VectorIndexHeader>(headerBytes.AsSpan(MagicSize));
    }

    /// <summary>读加载数据 — 向量段+Meta段+字符串区（跳过 SourceText 段）。</summary>
    private static async Task<byte[]> ReadLoadDataAsync(Stream fs, VectorIndexHeader header, CancellationToken ct) {
        const int MagicSize = 8;
        var headerSize = MagicSize + Unsafe.SizeOf<VectorIndexHeader>();
        var loadDataLen = (int)(header.SourceOffset - headerSize);
        var loadData = new byte[loadDataLen];
        await fs.ReadExactlyAsync(loadData, ct).ConfigureAwait(false);
        return loadData;
    }

    /// <summary>从 span 解析 MetaEntryFixed[] → ChunkMetadata — MemoryMarshal.Cast 零拷贝映射，字符串区不拷贝。</summary>
    private (List<(string, ChunkMetadata)>, List<(string, string)>, List<(string, float[])>, List<(string, string)>, Dictionary<string, float[]>?) ParseMetadataEntriesFromSpan(
        VectorIndexHeader header, ReadOnlySpan<byte> data, CancellationToken ct) {
        var count = header.Count;
        var dims = header.Dims;
        const int MagicSize = 8;
        var headerSize = MagicSize + Unsafe.SizeOf<VectorIndexHeader>();
        var vectorBytes = count * dims * sizeof(float);
        var entrySize = Unsafe.SizeOf<MetaEntryFixed>();

        var vectorSpan = MemoryMarshal.Cast<byte, float>(data.Slice(headerSize, vectorBytes));
        var entrySpan = MemoryMarshal.Cast<byte, MetaEntryFixed>(data.Slice(headerSize + vectorBytes, count * entrySize));
        var stringSpan = data.Slice(headerSize + vectorBytes + count * entrySize);

        var supportsGraphPersistence = _ann is IAnnSearchGraphPersistence;
        var vectorsDict = supportsGraphPersistence ? new Dictionary<string, float[]>(count) : null;
        var metadataItems = new List<(string, ChunkMetadata)>(count);
        var hashItems = new List<(string, string)>(count);
        var vectorItems = new List<(string, float[])>(count);
        var fileChunksItems = new List<(string, string)>(count);

        for (var i = 0; i < count; i++) {
            ct.ThrowIfCancellationRequested();
            var entry = entrySpan[i];
            var vector = vectorSpan.Slice(i * dims, dims).ToArray();

            var chunkId = ReadStringFromSpan(stringSpan, entry.ChunkIdOffset, entry.ChunkIdLen);
            var filePath = ReadStringFromSpan(stringSpan, entry.FilePathOffset, entry.FilePathLen);
            var fqn = ReadStringFromSpan(stringSpan, entry.FqnOffset, entry.FqnLen);
            var symbolKind = ReadStringFromSpan(stringSpan, entry.SymbolKindOffset, entry.SymbolKindLen);
            var parentChunkId = entry.ParentChunkIdLen > 0
                ? ReadStringFromSpan(stringSpan, entry.ParentChunkIdOffset, entry.ParentChunkIdLen)
                : null;
            var hash = ReadStringFromSpan(stringSpan, entry.HashOffset, entry.HashLen);
            var containedFqns = ReadContainedFqnsFromSpan(stringSpan, entry.ContainedFqnsOffset, entry.ContainedFqnCount);

            if (supportsGraphPersistence) {
                vectorsDict![chunkId] = vector;
            } else {
                _ann.Add(chunkId, vector);
            }
            metadataItems.Add((chunkId, new ChunkMetadata {
                ChunkId = chunkId, FilePath = filePath, SymbolFqn = fqn,
                SymbolKind = symbolKind,
                StartLine = entry.StartLine, EndLine = entry.EndLine,
                ParentChunkId = parentChunkId, SourceText = null,
                SourceTextOffset = entry.SourceTextOffset, SourceTextLen = entry.SourceTextLen,
                ContainedSymbolFqns = containedFqns
            }));
            hashItems.Add((chunkId, hash));
            vectorItems.Add((chunkId, vector));
            fileChunksItems.Add((filePath, chunkId));
        }
        return (metadataItems, hashItems, vectorItems, fileChunksItems, vectorsDict);
    }

    /// <summary>从 span 读 ContainedFqns — BinaryPrimitives 小端读取，零拷贝。</summary>
    private static List<string> ReadContainedFqnsFromSpan(ReadOnlySpan<byte> stringSpan, long offset, int count) {
        var result = new List<string>(count);
        var pos = (int)offset;
        for (var f = 0; f < count; f++) {
            var fqnLen = BinaryPrimitives.ReadInt32LittleEndian(stringSpan.Slice(pos, 4));
            pos += 4;
            result.Add(Encoding.UTF8.GetString(stringSpan.Slice(pos, fqnLen)));
            pos += fqnLen;
        }
        return result;
    }

    /// <summary>加载图段 — HNSW 图持久化或逐条重建。</summary>
    private async Task LoadGraphSegmentAsync(Stream fs, VectorIndexHeader header, Dictionary<string, float[]>? vectorsDict, CancellationToken ct) {
        if (header.GraphOffset <= 0 || _ann is not IAnnSearchGraphPersistence gp) return;
        fs.Seek(header.GraphOffset, SeekOrigin.Begin);
        var hasGraph = fs.ReadByte();
        if (hasGraph == 1) {
            var graphDataLen = (int)(fs.Length - header.GraphOffset - 1);
            var graphData = new byte[graphDataLen];
            await fs.ReadExactlyAsync(graphData, ct).ConfigureAwait(false);
            await using var graphMs = new MemoryStream(graphData, writable: false);
            using var graphBr = new BinaryReader(graphMs, Encoding.UTF8);
            gp.LoadGraph(graphBr, vectorsDict!);
        } else {
            foreach (var (id, vec) in vectorsDict!) {
                ct.ThrowIfCancellationRequested();
                _ann.Add(id, vec);
            }
        }
    }

    /// <summary>从 span 读 UTF8 字符串 — 热路径内联，零拷贝。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ReadStringFromSpan(ReadOnlySpan<byte> buffer, long offset, int len) {
        if (len <= 0 || offset < 0) return string.Empty;
        return Encoding.UTF8.GetString(buffer.Slice((int)offset, len));
    }

    /// <summary>
    /// 检查指定目录是否存在向量索引文件。
    /// </summary>
    public Task<bool> ExistsAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var filePath = Path.Combine(dirPath, "vector_index.bin");
        return Task.FromResult(_fs.FileExists(filePath));
    }

    private static void WriteString(BinaryWriter bw, string s) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static void WriteNullableString(BinaryWriter bw, string? s) {
        if (s is null) { bw.Write(-1); return; }
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static string ReadString(BinaryReader br) {
        var len = br.ReadInt32();
        return System.Text.Encoding.UTF8.GetString(br.ReadBytes(len));
    }

    private static string? ReadNullableString(BinaryReader br) {
        var len = br.ReadInt32();
        if (len < 0) return null;
        return System.Text.Encoding.UTF8.GetString(br.ReadBytes(len));
    }

    /// <summary>
    /// 按偏移量从索引文件读取 SourceText — 分页持久化，搜索时不加载 SourceText 段。
    /// </summary>
    private async Task<string?> ReadSourceTextAsync(ChunkMetadata meta, CancellationToken ct) {
        if (meta.SourceText is not null) return meta.SourceText;
        if (meta.SourceTextOffset < 0 || meta.SourceTextLen == 0 || _indexFilePath is null) return null;
        try {
            await using var stream = _fs.OpenRead(_indexFilePath);
            stream.Seek(meta.SourceTextOffset, SeekOrigin.Begin);
            var buffer = new byte[meta.SourceTextLen];
            await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            return System.Text.Encoding.UTF8.GetString(buffer);
        } catch {
            return null;
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
                if (!string.IsNullOrEmpty(symbolKindFilter) && !MatchesSymbolKind(meta.SymbolKind, symbolKindFilter!)) continue;
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
                ? TryAttachParentDocument(result, meta.ParentChunkId)
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
    /// 检查符号类型是否匹配（不区分大小写）。
    /// </summary>
    private static bool MatchesSymbolKind(string symbolKind, string symbolKindFilter) {
        return symbolKind.Equals(symbolKindFilter, StringComparison.OrdinalIgnoreCase);
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
