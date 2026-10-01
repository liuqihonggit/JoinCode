namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// VECIDX5 文件头 — 分页索引格式，48 字节 Pack=8 对齐。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly struct VectorIndexHeaderV5 {
    /// <summary>每页块数（默认 256）。</summary>
    public readonly int PageCapacity;
    /// <summary>页数。</summary>
    public readonly int PageCount;
    /// <summary>总块数。</summary>
    public readonly int TotalCount;
    /// <summary>向量维度。</summary>
    public readonly int Dims;
    /// <summary>跳表区偏移量。</summary>
    public readonly long SkipListOffset;
    /// <summary>数据页区偏移量。</summary>
    public readonly long PagesOffset;
    /// <summary>SourceText 段偏移量。</summary>
    public readonly long SourceOffset;
    /// <summary>图段偏移量（0=无图）。</summary>
    public readonly long GraphOffset;

    /// <summary>构造 VECIDX5 文件头。</summary>
    public VectorIndexHeaderV5(int pageCapacity, int pageCount, int totalCount, int dims,
        long skipListOffset, long pagesOffset, long sourceOffset, long graphOffset) {
        PageCapacity = pageCapacity;
        PageCount = pageCount;
        TotalCount = totalCount;
        Dims = dims;
        SkipListOffset = skipListOffset;
        PagesOffset = pagesOffset;
        SourceOffset = sourceOffset;
        GraphOffset = graphOffset;
    }
}

/// <summary>
/// VECIDX5 数据页头 — 48 字节 Pack=8 对齐，记录页内各段绝对偏移。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly struct PageHeaderV5 {
    /// <summary>页编号。</summary>
    public readonly int PageNo;
    /// <summary>页内块数。</summary>
    public readonly int Count;
    /// <summary>向量维度。</summary>
    public readonly int Dims;
    /// <summary>对齐填充。</summary>
    public readonly int _padding;
    /// <summary>向量数据绝对偏移量。</summary>
    public readonly long VectorOffset;
    /// <summary>MetaEntryFixed[] 绝对偏移量。</summary>
    public readonly long MetaOffset;
    /// <summary>字符串区绝对偏移量。</summary>
    public readonly long StringOffset;
    /// <summary>字符串区长度。</summary>
    public readonly long StringLen;

    /// <summary>构造数据页头。</summary>
    public PageHeaderV5(int pageNo, int count, int dims,
        long vectorOffset, long metaOffset, long stringOffset, long stringLen) {
        PageNo = pageNo;
        Count = count;
        Dims = dims;
        _padding = 0;
        VectorOffset = vectorOffset;
        MetaOffset = metaOffset;
        StringOffset = stringOffset;
        StringLen = stringLen;
    }
}

/// <summary>
/// 跳表条目 — 块ID → 页编号+页内索引，用于跳表持久化。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal readonly struct SkipListEntry {
    /// <summary>页编号。</summary>
    public readonly int PageNo;
    /// <summary>页内索引。</summary>
    public readonly int PageIndex;
    /// <summary>对齐填充。</summary>
    public readonly long _padding;

    /// <summary>构造跳表条目。</summary>
    public SkipListEntry(int pageNo, int pageIndex) {
        PageNo = pageNo;
        PageIndex = pageIndex;
        _padding = 0;
    }
}
