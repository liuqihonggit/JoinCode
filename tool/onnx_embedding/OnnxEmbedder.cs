namespace OnnxEmbedding;

/// <summary>
/// ONNX 嵌入器 — 加载量化模型 + BERT tokenizer，执行批量嵌入推理。
/// <para>使用 all-MiniLM-L6-v2 量化模型（22MB INT8，384维）。</para>
/// </summary>
internal sealed class OnnxEmbedder : IDisposable {

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly bool _hasTokenTypeIds;
    private readonly int _dimensions;
    private int _disposed;

    /// <summary>向量维度。</summary>
    public int Dimensions => _dimensions;

    /// <summary>
    /// 构造 ONNX 嵌入器 — 加载模型和 BERT 词表。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件路径。</param>
    /// <param name="vocabPath">BERT 词表文件路径（vocab.txt）。</param>
    public OnnxEmbedder(string modelPath, string vocabPath) {
        var options = new SessionOptions {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session = new InferenceSession(modelPath, options);
        _hasTokenTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");

        var bertOptions = new BertOptions {
            LowerCaseBeforeTokenization = true,
            UnknownToken = "[UNK]",
            ClassificationToken = "[CLS]",
            SeparatorToken = "[SEP]",
            PaddingToken = "[PAD]",
        };
        _tokenizer = BertTokenizer.Create(vocabPath, bertOptions);

        var outputName = _session.OutputMetadata.Keys.First();
        _dimensions = _session.OutputMetadata[outputName].Dimensions[^1];
        if (_dimensions <= 0) _dimensions = 384;
    }

    /// <summary>
    /// 批量嵌入文本 → 向量数组。
    /// <para> tokenize → ONNX 推理 → mean pooling → 返回向量。</para>
    /// </summary>
    /// <param name="texts">待嵌入的文本列表。</param>
    /// <param name="maxSeqLen">最大序列长度（默认 64）。</param>
    /// <returns>向量数组，每个向量长度为 <see cref="Dimensions"/>。</returns>
    public float[][] EmbedBatch(IReadOnlyList<string> texts, int maxSeqLen = 64) {
        var batchSize = texts.Count;
        var inputIds = new long[batchSize * maxSeqLen];
        var attentionMask = new long[batchSize * maxSeqLen];
        var tokenTypeIds = new long[batchSize * maxSeqLen];

        for (var i = 0; i < batchSize; i++) {
            var tokens = _tokenizer.EncodeToIds(texts[i], addSpecialTokens: true, considerPreTokenization: false, considerNormalization: false);
            var seqLen = Math.Min(tokens.Count, maxSeqLen);
            for (var j = 0; j < seqLen; j++) {
                inputIds[i * maxSeqLen + j] = tokens[j];
                attentionMask[i * maxSeqLen + j] = 1;
            }
        }

        var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new DenseTensor<long>(inputIds, new[] { batchSize, maxSeqLen })),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new DenseTensor<long>(attentionMask, new[] { batchSize, maxSeqLen })),
        };
        if (_hasTokenTypeIds) {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
                new DenseTensor<long>(tokenTypeIds, new[] { batchSize, maxSeqLen })));
        }

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();
        var vectors = new float[batchSize][];
        for (var i = 0; i < batchSize; i++) {
            var vector = new float[_dimensions];
            for (var d = 0; d < _dimensions; d++) {
                vector[d] = output[i, d];
            }
            vectors[i] = vector;
        }
        return vectors;
    }

    /// <summary>释放 ONNX session。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _session.Dispose();
    }
}
