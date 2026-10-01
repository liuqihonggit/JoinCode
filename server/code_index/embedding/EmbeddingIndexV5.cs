namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// VECIDX5 分页索引 — EmbeddingIndex 的 partial 方法。
/// <para>格式：Magic + HeaderV5 + 跳表区 + 页头数组 + 向量段 + Meta段 + 字符串区 + SourceText段 + 图段。</para>
/// <para>向量段和 Meta 段连续存储（MemoryMarshal.Cast 零拷贝），跳表索引块ID→页编号+页内索引。</para>
/// </summary>
public sealed partial class EmbeddingIndex {

    /// <summary>默认每页块数。</summary>
    private const int DefaultPageCapacity = 256;

    /// <summary>
    /// VECIDX5 分页持久化 — 跳表索引表 + 页头数组 + 连续向量/Meta 段。
    /// </summary>
    /// <param name="dirPath">目标目录路径。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task SaveAsyncV5(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var chunks = CollectChunksForSave();
        var totalCount = chunks.Count;
        var dims = totalCount > 0 ? chunks[0].Vector.Length : 0;
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

        var graphRegion = await BuildGraphRegionAsync().ConfigureAwait(false);

        await using var skipListRegion = new MemoryStream();
        await using var skipListBw = new BinaryWriter(skipListRegion, Encoding.UTF8);
        WriteSkipList(skipListBw, skipList);
        skipListBw.Flush();

        var layout = ComputeV5Layout(totalCount, dims, pageCount, skipListRegion.Length, stringRegion.Length, sourceRegion.Length, graphRegion.Length);

        _fs.CreateDirectory(dirPath);
        var filePath = Path.Combine(dirPath, "vector_index.bin");
        await WriteV5IndexFileAsync(filePath, layout, chunks, dims, entries, pageHeaders, skipList, stringRegion, sourceRegion, graphRegion, ct).ConfigureAwait(false);
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
    private async Task WriteV5IndexFileAsync(
        string filePath, V5Layout layout,
        List<(string ChunkId, float[] Vector, ChunkMetadata Meta, string Hash)> chunks,
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

    /// <summary>
    /// VECIDX5 分页加载 — mmap 零拷贝 + 跳表重建。
    /// <para>跳表指向 mmap 数据页，搜索时 O(log n) 定位块。</para>
    /// </summary>
    /// <param name="dirPath">源目录路径。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示加载成功。</returns>
    public async Task<bool> LoadAsyncV5(string dirPath, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(dirPath);
        var filePath = Path.Combine(dirPath, "vector_index.bin");
        if (!_fs.FileExists(filePath)) return false;
        _indexFilePath = filePath;

        VectorIndexHeaderV5 header;
        SkipList<string, SkipListEntry> skipList;
        List<(string, ChunkMetadata)> metadataItems;
        List<(string, string)> hashItems;
        List<(string, float[])> vectorItems;
        List<(string, string)> fileChunksItems;
        Dictionary<string, float[]>? vectorsDict;

        using (var mmap = _fs.OpenMemoryMappedRead(filePath)) {
            var data = mmap.AsSpan();
            header = ReadHeaderV5FromSpan(data);
            if (header.TotalCount == 0) return false;

            skipList = ReadSkipListFromSpan(data, header.SkipListOffset, ct);

            var pageHeaders = ReadPageHeadersFromSpan(data, header.PagesOffset, header.PageCount);
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

            (metadataItems, hashItems, vectorItems, fileChunksItems, vectorsDict) =
                ParseMetadataEntriesV5(header, vectorSpan, entrySpan, stringSpan, ct);
        }

        await using var fs = _fs.OpenRead(filePath);
        await LoadGraphSegmentV5Async(fs, header, vectorsDict, ct).ConfigureAwait(false);

        CasBulkUpdateMetadata(metadataItems);
        CasBulkUpdateHashes(hashItems);
        CasBulkUpdateVectors(vectorItems);
        CasBulkUpdateFileChunks(fileChunksItems);
        Interlocked.Exchange(ref _status, (int)IndexStatus.Ready);
        return true;
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

    /// <summary>解析 VECIDX5 Meta 条目 — 跳表已重建，向量段和 Meta 段零拷贝映射。</summary>
    private (List<(string, ChunkMetadata)>, List<(string, string)>, List<(string, float[])>, List<(string, string)>, Dictionary<string, float[]>?) ParseMetadataEntriesV5(
        VectorIndexHeaderV5 header, ReadOnlySpan<float> vectorSpan, ReadOnlySpan<MetaEntryFixed> entrySpan,
        ReadOnlySpan<byte> stringSpan, CancellationToken ct) {
        var count = header.TotalCount;
        var dims = header.Dims;

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

    /// <summary>加载 VECIDX5 图段。</summary>
    private async Task LoadGraphSegmentV5Async(Stream fs, VectorIndexHeaderV5 header, Dictionary<string, float[]>? vectorsDict, CancellationToken ct) {
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
}
