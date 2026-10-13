namespace JoinCode.CodeIndex;

/// <summary>
/// 代码索引超参数集中存放 — 切块、Embedding、ONNX 推理、ANN、搜索、持久化。
/// <para>所有超参数在此一处声明，带含义注释和合理范围，方便调优时了解上下文。</para>
/// <para>支持从环境变量覆盖（JCC_ 前缀），未设置时用默认值。</para>
/// </summary>
public sealed record CodeIndexHyperParams {
    // ── 切块参数 ──────────────────────────────────────────────

    /// <summary>每块行数（固定行数切块）。越小越接近函数级（召回率↑但块数↑索引慢）。范围 100~500。</summary>
    public int ChunkSize { get; init; } = 400;

    /// <summary>相邻块重叠行数，避免函数跨越块边界被切断。通常 ChunkSize 的 5~10%。</summary>
    public int Overlap { get; init; } = 25;

    // ── Embedding 参数 ────────────────────────────────────────

    /// <summary>最大序列长度（token 数）。all-MiniLM-L6-v2 上限 256。越大召回率↑但推理慢。范围 32~256。</summary>
    public int MaxSeqLen { get; init; } = 128;

    /// <summary>嵌入批大小 — 单次送入 ONNX 推理的文本数量。越大吞吐↑但内存↑。范围 64~512。</summary>
    public int EmbedBatchSize { get; init; } = 256;

    /// <summary>向量维度（all-MiniLM-L6-v2 = 384）。从 ONNX OutputMetadata 读取，此为回退默认值。</summary>
    public int Dimensions { get; init; } = 384;

    /// <summary>动态 padding 对齐粒度（SIMD 友好）。必须为 2 的幂。对齐省 30~70% 推理时间。</summary>
    public int PaddingAlign { get; init; } = 8;

    /// <summary>并行 tokenize 阈值。batch ≥ 此值时用 AsParallel() 并行编码，否则串行。</summary>
    public int ParallelTokenizeThreshold { get; init; } = 8;

    // ── ONNX 推理参数 ────────────────────────────────────────

    /// <summary>ONNX 并行推理实例数。0=自动（取 ProcessorCount）。越大 wall time↓但内存↑。</summary>
    public int OnnxDegree { get; init; } = 0;

    // ── ANN 参数 ─────────────────────────────────────────────

    /// <summary>ANN 算法类型："brute"=暴力精确，"hnsw"=近似快速。brute 召回率 100% 但慢。</summary>
    public string AnnType { get; init; } = "brute";

    /// <summary>HNSW 每层最大连接数（第0层 = 2m）。影响图密度和内存。范围 8~48。</summary>
    public int HnswM { get; init; } = 16;

    /// <summary>HNSW 建图时搜索宽度。越大召回率↑但建图越慢。范围 100~400。</summary>
    public int HnswEfConstruction { get; init; } = 200;

    /// <summary>HNSW 搜索时搜索宽度。越大召回率↑但搜索越慢。范围 20~200。</summary>
    public int HnswEfSearch { get; init; } = 50;

    // ── 搜索参数 ─────────────────────────────────────────────

    /// <summary>过采样倍数 — 有过滤条件时取 oversampleK × topK 再过滤，保证过滤后仍有足够结果。</summary>
    public int OversampleK { get; init; } = 3;

    /// <summary>默认 Top-K 搜索结果数。</summary>
    public int DefaultTopK { get; init; } = 20;

    // ── 持久化参数 ───────────────────────────────────────────

    /// <summary>嵌入持久化后端："bin"=mmap 二进制，"lsm"=PithosDB LSM-Tree。</summary>
    public string EmbeddingBackend { get; init; } = "lsm";

    // ── 其他参数 ─────────────────────────────────────────────

    /// <summary>AST 语法树缓存最大条目数。</summary>
    public int TreeCacheMaxEntries { get; init; } = 500;

    /// <summary>文件变更防抖间隔（毫秒）。</summary>
    public int FileWatchDebounceMs { get; init; } = 500;

    /// <summary>增量更新最大并行度上限。</summary>
    public int IncrementalParallelism { get; init; } = 4;

    /// <summary>
    /// 从环境变量加载超参数 — JCC_ 前缀变量覆盖默认值，未设置则用默认。
    /// </summary>
    public static CodeIndexHyperParams FromEnvironment() => new() {
        ChunkSize = ReadInt("JCC_CHUNK_SIZE", 400),
        Overlap = ReadInt("JCC_CHUNK_OVERLAP", 25),
        MaxSeqLen = ReadInt("JCC_ONNX_MAX_SEQ_LEN", 128),
        EmbedBatchSize = ReadInt("JCC_EMBED_BATCH_SIZE", 256),
        Dimensions = ReadInt("JCC_EMBED_DIMENSIONS", 384),
        PaddingAlign = ReadInt("JCC_PADDING_ALIGN", 8),
        ParallelTokenizeThreshold = ReadInt("JCC_PARALLEL_TOKENIZE_THRESHOLD", 8),
        OnnxDegree = ReadInt("JCC_ONNX_DEGREE", 0),
        AnnType = Environment.GetEnvironmentVariable("JCC_ANN_TYPE") ?? "brute",
        HnswM = ReadInt("JCC_HNSW_M", 16),
        HnswEfConstruction = ReadInt("JCC_HNSW_EF_CONSTRUCTION", 200),
        HnswEfSearch = ReadInt("JCC_HNSW_EF_SEARCH", 50),
        OversampleK = ReadInt("JCC_OVERSAMPLE_K", 3),
        DefaultTopK = ReadInt("JCC_DEFAULT_TOP_K", 20),
        EmbeddingBackend = Environment.GetEnvironmentVariable("JCC_EMBEDDING_BACKEND") ?? "lsm",
        TreeCacheMaxEntries = ReadInt("JCC_TREE_CACHE_MAX_ENTRIES", 500),
        FileWatchDebounceMs = ReadInt("JCC_FILE_WATCH_DEBOUNCE_MS", 500),
        IncrementalParallelism = ReadInt("JCC_INCREMENTAL_PARALLELISM", 4),
    };

    private static int ReadInt(string key, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var v) && v > 0 ? v : fallback;
}
