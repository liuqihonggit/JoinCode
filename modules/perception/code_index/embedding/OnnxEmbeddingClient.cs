namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// ONNX 嵌入模型 — 直接引用 OnnxEmbedder 类库，无 IPC 开销。
/// <para>AOT 兼容（ONNX Runtime 1.22.1 支持 NativeAOT）。</para>
/// <para>实现 IEmbeddingModel 接口，供 EmbeddingIndex 使用。</para>
/// <para>多实例并行推理：持有 N 个 OnnxEmbedder，每个 IntraOpNumThreads=ProcessorCount/N，EmbedBatchAsync 分 N 组并行推理，wall time 降至 1/N。</para>
/// </summary>
public sealed class OnnxEmbeddingClient : IEmbeddingModel, IAsyncDisposable {

    private readonly OnnxEmbedder[] _embedders;
    private readonly int _degree;
    private readonly int _maxSeqLen;
    private readonly string _modelId;
    private readonly ILogger<OnnxEmbeddingClient>? _logger;
    private int _disposed;

    /// <summary>向量维度。</summary>
    public int Dimensions => _embedders[0].Dimensions;

    /// <summary>模型标识（用于缓存键）。</summary>
    public string ModelId => _modelId;

    /// <summary>并行实例数。</summary>
    public int Degree => _degree;

    /// <summary>最大序列长度。</summary>
    public int MaxSeqLen => _maxSeqLen;

    /// <summary>
    /// 构造 ONNX 嵌入模型 — 加载量化模型和 BERT 词表，创建 N 个推理实例并行。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件路径。</param>
    /// <param name="vocabPath">BERT 词表文件路径（vocab.txt）。</param>
    /// <param name="fs">文件系统抽象（检查文件是否存在）。</param>
    /// <param name="modelId">模型标识（默认 onnx-minilm-l6-v2）。</param>
    /// <param name="degree">并行推理实例数（默认 0=自动，取 ProcessorCount/2；每个实例 IntraOpNumThreads=ProcessorCount/degree，避免线程争抢）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public OnnxEmbeddingClient(
        string modelPath,
        string vocabPath,
        IFileSystem fs,
        string modelId = "onnx-minilm-l6-v2",
        int degree = 0,
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

        _degree = degree <= 0
            ? Math.Max(1, Environment.ProcessorCount)
            : Math.Clamp(degree, 1, Environment.ProcessorCount);
        var threadsPerInstance = Math.Max(1, Environment.ProcessorCount / _degree);
        _embedders = new OnnxEmbedder[_degree];
        for (var i = 0; i < _degree; i++) {
            _embedders[i] = new OnnxEmbedder(modelPath, vocabPath, threadsPerInstance);
        }
        _modelId = modelId;
        _logger = logger;
        _maxSeqLen = int.TryParse(Environment.GetEnvironmentVariable("JCC_ONNX_MAX_SEQ_LEN"), out var msl) && msl > 0
            ? msl : 8;
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
        return await EmbedBatchAsync([text], ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 批量嵌入 — 分 N 组并行推理，每组调一个 OnnxEmbedder 实例。
    /// <para>degree=1 时直接单实例推理，无并行开销。</para>
    /// <para>degree&gt;1 时分 N 组，Task.Run 并行，wall time 降至 1/N。</para>
    /// <para>返回扁平化向量 [count × Dimensions]，第 i 个向量在 [i*Dimensions, (i+1)*Dimensions)。</para>
    /// </summary>
    /// <param name="texts">待嵌入文本列表。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>扁平化向量数组 [count × Dimensions]。</returns>
    public Task<float[]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0) return Task.FromResult(Array.Empty<float>());
        ct.ThrowIfCancellationRequested();

        if (_degree == 1) return Task.FromResult(_embedders[0].EmbedBatch(texts, _maxSeqLen));

        return EmbedBatchParallelAsync(texts, ct);
    }

    private async Task<float[]> EmbedBatchParallelAsync(IReadOnlyList<string> texts, CancellationToken ct) {
        var count = texts.Count;
        var dim = Dimensions;
        var flat = new float[count * dim];
        var groupSize = (count + _degree - 1) / _degree;
        var tasks = new List<Task>(_degree);

        for (var g = 0; g < _degree; g++) {
            var start = g * groupSize;
            var end = Math.Min(start + groupSize, count);
            if (start >= end) break;
            var embedder = _embedders[g];
            tasks.Add(Task.Run(() => {
                var subTexts = new string[end - start];
                for (var i = 0; i < subTexts.Length; i++) {
                    subTexts[i] = texts[start + i];
                }
                var subFlat = embedder.EmbedBatch(subTexts, _maxSeqLen);
                subFlat.AsSpan().CopyTo(flat.AsSpan(start * dim));
            }, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return flat;
    }

    /// <summary>释放所有 ONNX session。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var embedder in _embedders) {
            embedder.DisposeSafe(_logger);
        }
    }

    /// <summary>异步释放。</summary>
    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
