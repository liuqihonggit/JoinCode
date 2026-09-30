namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// ONNX 嵌入模型 — 直接引用 OnnxEmbedder 类库，无 IPC 开销。
/// <para>AOT 兼容（ONNX Runtime 1.22.1 支持 NativeAOT）。</para>
/// <para>实现 IEmbeddingModel 接口，供 EmbeddingIndex 使用。</para>
/// </summary>
public sealed class OnnxEmbeddingClient : IEmbeddingModel, IAsyncDisposable {

    private readonly OnnxEmbedder _embedder;
    private readonly string _modelId;
    private readonly ILogger<OnnxEmbeddingClient>? _logger;
    private int _disposed;

    /// <summary>向量维度。</summary>
    public int Dimensions => _embedder.Dimensions;

    /// <summary>模型标识（用于缓存键）。</summary>
    public string ModelId => _modelId;

    /// <summary>
    /// 构造 ONNX 嵌入模型 — 加载量化模型和 BERT 词表。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件路径。</param>
    /// <param name="vocabPath">BERT 词表文件路径（vocab.txt）。</param>
    /// <param name="fs">文件系统抽象（检查文件是否存在）。</param>
    /// <param name="modelId">模型标识（默认 onnx-minilm-l6-v2）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public OnnxEmbeddingClient(
        string modelPath,
        string vocabPath,
        IFileSystem fs,
        string modelId = "onnx-minilm-l6-v2",
        ILogger<OnnxEmbeddingClient>? logger = null) {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentNullException.ThrowIfNull(vocabPath);
        ArgumentNullException.ThrowIfNull(fs);
        if (!fs.FileExists(modelPath)) {
            throw new FileNotFoundException("ONNX 模型文件不存在", modelPath);
        }
        if (!fs.FileExists(vocabPath)) {
            throw new FileNotFoundException("BERT 词表文件不存在", vocabPath);
        }

        _embedder = new OnnxEmbedder(modelPath, vocabPath);
        _modelId = modelId;
        _logger = logger;
    }

    /// <summary>
    /// 嵌入单个文本 → 向量。
    /// </summary>
    /// <param name="text">待嵌入文本。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>嵌入向量。</returns>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(text);
        ct.ThrowIfCancellationRequested();
        var results = await EmbedBatchAsync([text], ct).ConfigureAwait(false);
        return results[0];
    }

    /// <summary>
    /// 批量嵌入 — 直接调用 OnnxEmbedder 推理，无 IPC 开销。
    /// </summary>
    /// <param name="texts">待嵌入文本列表。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>向量数组。</returns>
    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0) return Task.FromResult(Array.Empty<float[]>());
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(_embedder.EmbedBatch(texts));
    }

    /// <summary>释放 ONNX session。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _embedder.DisposeSafe(_logger);
    }

    /// <summary>异步释放。</summary>
    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
