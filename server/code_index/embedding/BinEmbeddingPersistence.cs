namespace JoinCode.CodeIndex.Embedding;

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
/// bin 二进制持久化后端 — mmap 零拷贝加载，VECIDX5 分页格式。
/// <para>格式：Magic + HeaderV5 + 跳表区 + 页头数组 + 向量段 + Meta段 + 字符串区 + SourceText段 + 图段。</para>
/// <para>向量段和 Meta 段连续存储（MemoryMarshal.Cast 零拷贝），跳表索引块ID→页编号+页内索引。</para>
/// <para>增量写入通过扩展字典缓冲实现，FlushAsync 时合并已有 bin 文件全量重写。</para>
/// </summary>
internal sealed class BinEmbeddingPersistence : IEmbeddingPersistence {
    private readonly IFileSystem _fs;

    /// <summary>默认每页块数。</summary>
    private const int DefaultPageCapacity = 256;

    /// <summary>增量缓冲 — 扩展字典，追加/修改的 chunk 暂存于此。始终可用，无需显式打开。</summary>
    private readonly Dictionary<string, (float[] Vector, ChunkMetadata Meta, string Hash)> _incrementalBuffer = [];
    /// <summary>增量删除集 — 待删除的 chunkId。始终可用。</summary>
    private readonly HashSet<string> _incrementalDeletes = [];

    /// <summary>
    /// 构造 bin 持久化后端。
    /// </summary>
    /// <param name="fs">文件系统抽象 — 用于持久化读写。</param>
    public BinEmbeddingPersistence(IFileSystem fs) {
        ArgumentNullException.ThrowIfNull(fs);
        _fs = fs;
    }

    /// <summary>检查指定目录是否存在 vector_index.bin 文件。</summary>
    public Task<bool> ExistsAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var binPath = Path.Combine(dirPath, "vector_index.bin");
        return Task.FromResult(_fs.FileExists(binPath));
    }

    /// <summary>全量保存快照为 VECIDX5 二进制文件。</summary>
    public async Task SaveAsync(string dirPath, EmbeddingSnapshot snapshot, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var chunks = snapshot.Chunks;
        var totalCount = chunks.Count;
        var dims = snapshot.Dims;
        var pageCount = Math.Max(1, (totalCount + DefaultPageCapacity - 1) / DefaultPageCapacity);

        await using var stringRegion = new MemoryStream();
        await using var sourceRegion = new MemoryStream();
        var entries = new MetaEntryFixed[totalCount];
        var skipList = new SkipList<string, SkipListEntry>(totalCount);
        var pageHeaders = new PageHeaderV5[pageCount];

        for (var i = 0; i < totalCount; i++) {
            ct.ThrowIfCancellationRequested();
            entries[i] = BuildMetaEntry(chunks[i], stringRegion, sourceRegion);
            var pageNo = i / DefaultPageCapacity;
            var pageIndex = i % DefaultPageCapacity;
            skipList.Insert(chunks[i].ChunkId, new SkipListEntry(pageNo, pageIndex));
        }

        for (var p = 0; p < pageCount; p++) {
            var pageStart = p * DefaultPageCapacity;
            var pageEnd = Math.Min(pageStart + DefaultPageCapacity, totalCount);
            pageHeaders[p] = new PageHeaderV5(p, pageEnd - pageStart, dims, 0, 0, 0, 0);
        }

        var graphRegion = snapshot.GraphBytes ?? [];

        await using var skipListRegion = new MemoryStream();
        await using var skipListBw = new BinaryWriter(skipListRegion, Encoding.UTF8);
        WriteSkipList(skipListBw, skipList);
        skipListBw.Flush();

        var layout = ComputeV5Layout(totalCount, dims, pageCount, skipListRegion.Length, stringRegion.Length, sourceRegion.Length, graphRegion.Length);

        _fs.CreateDirectory(dirPath);
        var filePath = Path.Combine(dirPath, "vector_index.bin");
        await WriteIndexFileAsync(filePath, layout, chunks, dims, entries, pageHeaders, skipList, stringRegion, sourceRegion, graphRegion, ct).ConfigureAwait(false);
    }

    /// <summary>从 VECIDX5 文件加载快照 — mmap 零拷贝读取。</summary>
    public async Task<EmbeddingSnapshot?> LoadAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var filePath = Path.Combine(dirPath, "vector_index.bin");
        if (!_fs.FileExists(filePath)) return null;

        VectorIndexHeaderV5 header;
        List<(string ChunkId, float[] Vector, ChunkMetadata Meta, string Hash)> chunks;
        byte[]? graphBytes;

        using (var mmap = _fs.OpenMemoryMappedRead(filePath)) {
            var data = mmap.AsSpan();
            header = ReadHeaderV5FromSpan(data);
            if (header.TotalCount == 0) return null;

            _ = ReadSkipListFromSpan(data, header.SkipListOffset, ct);

            var dims = header.Dims;
            var totalCount = header.TotalCount;
            var entrySize = Unsafe.SizeOf<MetaEntryFixed>();
            var pageHeaderSize = Unsafe.SizeOf<PageHeaderV5>();
            var vectorBytes = totalCount * dims * sizeof(float);

            var vectorOffset = header.PagesOffset + header.PageCount * pageHeaderSize;
            var vectorSpan = MemoryMarshal.Cast<byte, float>(data.Slice((int)vectorOffset, vectorBytes));
            var metaOffset = vectorOffset + vectorBytes;
            var entrySpan = MemoryMarshal.Cast<byte, MetaEntryFixed>(data.Slice((int)metaOffset, totalCount * entrySize));
            var stringOffset = metaOffset + totalCount * entrySize;
            var stringSpan = data.Slice((int)stringOffset);

            chunks = ParseChunksV5(header, vectorSpan, entrySpan, stringSpan, ct);
            graphBytes = ReadGraphBytes(data, header);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return new EmbeddingSnapshot {
            Dims = header.Dims,
            Chunks = chunks,
            GraphBytes = graphBytes
        };
    }

    /// <summary>从 mmap span 读取图段字节 — GraphOffset>0 时读取到文件末尾。</summary>
    private static byte[]? ReadGraphBytes(ReadOnlySpan<byte> data, VectorIndexHeaderV5 header) {
        if (header.GraphOffset <= 0) return null;
        var offset = (int)header.GraphOffset;
        if (offset >= data.Length) return null;
        var graphLen = data.Length - offset;
        if (graphLen == 0) return null;
        var result = new byte[graphLen];
        data.Slice(offset, graphLen).CopyTo(result);
        return result;
    }

    /// <summary>VECIDX5 布局 — 各段绝对偏移量。</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private readonly struct V5Layout {
        /// <summary>头总大小。</summary>
        public readonly long HeaderSize;
        /// <summary>跳表区偏移。</summary>
        public readonly long SkipListOffset;
        /// <summary>页头数组偏移。</summary>
        public readonly long PageHeadersOffset;
        /// <summary>向量段偏移。</summary>
        public readonly long VectorOffset;
        /// <summary>Meta 段偏移。</summary>
        public readonly long MetaOffset;
        /// <summary>字符串区偏移。</summary>
        public readonly long StringOffset;
        /// <summary>SourceText 段偏移。</summary>
        public readonly long SourceOffset;
        /// <summary>图段偏移。</summary>
        public readonly long GraphOffset;
        /// <summary>构造布局。</summary>
        public V5Layout(long headerSize, long skipListOffset, long pageHeadersOffset, long vectorOffset, long metaOffset, long stringOffset, long sourceOffset, long graphOffset) {
            HeaderSize = headerSize; SkipListOffset = skipListOffset; PageHeadersOffset = pageHeadersOffset;
            VectorOffset = vectorOffset; MetaOffset = metaOffset; StringOffset = stringOffset;
            SourceOffset = sourceOffset; GraphOffset = graphOffset;
        }
    }

    /// <summary>计算 VECIDX5 各段偏移量。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static V5Layout ComputeV5Layout(int totalCount, int dims, int pageCount, long skipListLen, long stringLen, long sourceLen, long graphLen) {
        const int MagicSize = 8;
        var headerSize = MagicSize + Unsafe.SizeOf<VectorIndexHeaderV5>();
        var skipListOffset = headerSize;
        var pageHeadersOffset = skipListOffset + skipListLen;
        var pageHeadersSize = (long)pageCount * Unsafe.SizeOf<PageHeaderV5>();
        var vectorOffset = pageHeadersOffset + pageHeadersSize;
        var vectorSize = (long)totalCount * dims * sizeof(float);
        var metaOffset = vectorOffset + vectorSize;
        var metaSize = (long)totalCount * Unsafe.SizeOf<MetaEntryFixed>();
        var stringOffset = metaOffset + metaSize;
        var sourceOffset = stringOffset + stringLen;
        var graphOffset = sourceOffset + sourceLen;
        return new V5Layout(headerSize, skipListOffset, pageHeadersOffset, vectorOffset, metaOffset, stringOffset, sourceOffset, graphOffset);
    }

    /// <summary>写入 VECIDX5 索引文件。</summary>
    private async Task WriteIndexFileAsync(
        string filePath, V5Layout layout,
        IReadOnlyList<(string ChunkId, float[] Vector, ChunkMetadata Meta, string Hash)> chunks,
        int dims, MetaEntryFixed[] entries, PageHeaderV5[] pageHeaders,
        SkipList<string, SkipListEntry> skipList,
        MemoryStream stringRegion, MemoryStream sourceRegion, byte[] graphRegion,
        CancellationToken ct) {
        await using var ms = new MemoryStream();
        await using var bw = new BinaryWriter(ms, Encoding.UTF8);
        bw.Write(Encoding.UTF8.GetBytes("VECIDX5\0"));
        var header = new VectorIndexHeaderV5(DefaultPageCapacity, pageHeaders.Length, chunks.Count, dims,
            layout.SkipListOffset, layout.PageHeadersOffset, layout.SourceOffset, layout.GraphOffset);
        WriteStruct(bw, header);

        WriteSkipList(bw, skipList);

        foreach (var ph in pageHeaders) {
            WriteStruct(bw, ph);
        }

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

    /// <summary>序列化跳表到 BinaryWriter — 节点数 + 每节点(keyLen+key+pageNo+pageIndex)。</summary>
    private static void WriteSkipList(BinaryWriter bw, SkipList<string, SkipListEntry> skipList) {
        bw.Write(skipList.Count);
        foreach (var (key, entry) in skipList.Enumerate()) {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            bw.Write(keyBytes.Length);
            bw.Write(keyBytes);
            bw.Write(entry.PageNo);
            bw.Write(entry.PageIndex);
        }
    }

    /// <summary>从 span 反序列化跳表 — mmap 零拷贝读取。</summary>
    private static SkipList<string, SkipListEntry> ReadSkipListFromSpan(ReadOnlySpan<byte> data, long offset, CancellationToken ct) {
        var pos = (int)offset;
        var count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
        pos += 4;
        var sl = new SkipList<string, SkipListEntry>(count);
        for (var i = 0; i < count; i++) {
            ct.ThrowIfCancellationRequested();
            var keyLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
            pos += 4;
            var key = Encoding.UTF8.GetString(data.Slice(pos, keyLen));
            pos += keyLen;
            var pageNo = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
            pos += 4;
            var pageIndex = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
            pos += 4;
            sl.Insert(key, new SkipListEntry(pageNo, pageIndex));
        }
        return sl;
    }

    /// <summary>从 span 读 VECIDX5 文件头。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VectorIndexHeaderV5 ReadHeaderV5FromSpan(ReadOnlySpan<byte> data) {
        const int MagicSize = 8;
        var magic = Encoding.UTF8.GetString(data.Slice(0, 7));
        if (magic != "VECIDX5") return default;
        return MemoryMarshal.Read<VectorIndexHeaderV5>(data.Slice(MagicSize, Unsafe.SizeOf<VectorIndexHeaderV5>()));
    }

    /// <summary>从 span 读页头数组。</summary>
    private static PageHeaderV5[] ReadPageHeadersFromSpan(ReadOnlySpan<byte> data, long offset, int pageCount) {
        var pageHeaderSize = Unsafe.SizeOf<PageHeaderV5>();
        var span = data.Slice((int)offset, pageCount * pageHeaderSize);
        var cast = MemoryMarshal.Cast<byte, PageHeaderV5>(span);
        return cast.ToArray();
    }

    /// <summary>解析 VECIDX5 Meta 条目 — 向量段和 Meta 段零拷贝映射，构建 chunks 列表。</summary>
    private static List<(string ChunkId, float[] Vector, ChunkMetadata Meta, string Hash)> ParseChunksV5(
        VectorIndexHeaderV5 header, ReadOnlySpan<float> vectorSpan, ReadOnlySpan<MetaEntryFixed> entrySpan,
        ReadOnlySpan<byte> stringSpan, CancellationToken ct) {
        var count = header.TotalCount;
        var dims = header.Dims;
        var chunks = new List<(string, float[], ChunkMetadata, string)>(count);

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

            chunks.Add((chunkId, vector, new ChunkMetadata {
                ChunkId = chunkId, FilePath = filePath, SymbolFqn = fqn,
                SymbolKind = symbolKind,
                StartLine = entry.StartLine, EndLine = entry.EndLine,
                ParentChunkId = parentChunkId, SourceText = null,
                SourceTextOffset = entry.SourceTextOffset, SourceTextLen = entry.SourceTextLen,
                ContainedSymbolFqns = containedFqns
            }, hash));
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
        var bytes = Encoding.UTF8.GetBytes(s);
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
            var bytes = Encoding.UTF8.GetBytes(fqn);
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
        var bytes = Encoding.UTF8.GetBytes(sourceText);
        ms.Write(bytes);
        len = bytes.Length;
    }

    /// <summary>泛型写结构体到 BinaryWriter — MemoryMarshal.AsBytes 零分配。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteStruct<T>(BinaryWriter bw, T value) where T : struct {
        bw.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
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

    /// <summary>从 span 读 UTF8 字符串 — 热路径内联，零拷贝。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ReadStringFromSpan(ReadOnlySpan<byte> buffer, long offset, int len) {
        if (len <= 0 || offset < 0) return string.Empty;
        return Encoding.UTF8.GetString(buffer.Slice((int)offset, len));
    }

    /// <summary>增量写入单个 chunk — 追加到扩展字典缓冲，FlushAsync 时合并刷盘。无需显式打开。</summary>
    public Task PersistChunkAsync(string dirPath, string chunkId, float[] vector, ChunkMetadata meta, string hash, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        ArgumentNullException.ThrowIfNull(chunkId);
        _incrementalBuffer[chunkId] = (vector, meta, hash);
        _incrementalDeletes.Remove(chunkId);
        return Task.CompletedTask;
    }

    /// <summary>增量删除单个 chunk — 加入删除集，FlushAsync 时排除。无需显式打开。</summary>
    public Task DeleteChunkAsync(string dirPath, string chunkId, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        ArgumentNullException.ThrowIfNull(chunkId);
        _incrementalBuffer.Remove(chunkId);
        _incrementalDeletes.Add(chunkId);
        return Task.CompletedTask;
    }

    /// <summary>刷盘 — 合并已有 bin 文件 + 增量缓冲 - 删除集，全量重写 bin 文件。</summary>
    public async Task FlushAsync(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        if (_incrementalBuffer.Count == 0 && _incrementalDeletes.Count == 0) return;

        var existing = await LoadAsync(dirPath, ct).ConfigureAwait(false);
        var dims = existing?.Dims ?? (_incrementalBuffer.Count > 0 ? _incrementalBuffer.First().Value.Vector.Length : 0);
        var graphBytes = existing?.GraphBytes;

        var merged = new Dictionary<string, (float[] Vector, ChunkMetadata Meta, string Hash)>();
        if (existing is not null) {
            foreach (var (chunkId, vector, meta, hash) in existing.Chunks) {
                merged[chunkId] = (vector, meta, hash);
            }
        }
        foreach (var (chunkId, data) in _incrementalBuffer) {
            ct.ThrowIfCancellationRequested();
            merged[chunkId] = data;
        }
        foreach (var chunkId in _incrementalDeletes) {
            merged.Remove(chunkId);
        }

        var chunks = merged.Select(kv => (kv.Key, kv.Value.Vector, kv.Value.Meta, kv.Value.Hash)).ToList();
        var snapshot = new EmbeddingSnapshot {
            Dims = dims,
            Chunks = chunks,
            GraphBytes = graphBytes
        };
        await SaveAsync(dirPath, snapshot, ct).ConfigureAwait(false);

        _incrementalBuffer.Clear();
        _incrementalDeletes.Clear();
    }

    /// <summary>释放资源 — bin 后端无托管资源需释放。</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
