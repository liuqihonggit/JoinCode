namespace OnnxEmbedding;

/// <summary>
/// ONNX 嵌入器 — 加载量化模型 + BERT tokenizer，执行批量嵌入推理。
/// <para>使用 all-MiniLM-L6-v2 量化模型（22MB INT8，384维）。</para>
/// </summary>
public sealed class OnnxEmbedder : IDisposable {

    private readonly InferenceSession _session;
    private readonly GuardedBertTokenizer _tokenizer;
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
            InterOpNumThreads = 1,
            IntraOpNumThreads = Environment.ProcessorCount,
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
        _tokenizer = new GuardedBertTokenizer(BertTokenizer.Create(vocabPath, bertOptions));

        var outputName = _session.OutputMetadata.Keys.First();
        _dimensions = _session.OutputMetadata[outputName].Dimensions[^1];
        if (_dimensions <= 0) _dimensions = 384;
    }

    /// <summary>
    /// 批量嵌入文本 → 向量数组。
    /// <para> tokenize → ONNX 推理 → SIMD mean pooling → 返回向量。</para>
    /// <para>mean pooling 用 Vector&lt;float&gt; 批量累加+归一化，比标量快 4-8x。</para>
    /// </summary>
    /// <param name="texts">待嵌入的文本列表。</param>
    /// <param name="maxSeqLen">最大序列长度（默认 32）。</param>
    /// <returns>向量数组，每个向量长度为 <see cref="Dimensions"/>。</returns>
    public float[][] EmbedBatch(IReadOnlyList<string> texts, int maxSeqLen = 32) {
        var batchSize = texts.Count;
        var inputIds = new long[batchSize * maxSeqLen];
        var attentionMask = new long[batchSize * maxSeqLen];
        var tokenTypeIds = new long[batchSize * maxSeqLen];

        for (var i = 0; i < batchSize; i++) {
            var tokens = _tokenizer.EncodeToIdsSafe(texts[i]);
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
        var outputDims = output.Dimensions;
        var vectors = new float[batchSize][];

        if (outputDims.Length == 3) {
            var seqLenDim = outputDims[1];
            if (output is DenseTensor<float> denseOutput && Vector.IsHardwareAccelerated) {
                var outputSpan = denseOutput.Buffer.Span;
                for (var i = 0; i < batchSize; i++) {
                    var vector = new float[_dimensions];
                    MeanPoolSimd(outputSpan, attentionMask, i, seqLenDim, maxSeqLen, _dimensions, vector);
                    vectors[i] = vector;
                }
            } else {
                MeanPoolScalar(output, attentionMask, batchSize, seqLenDim, maxSeqLen, _dimensions, vectors);
            }
        } else {
            for (var i = 0; i < batchSize; i++) {
                var vector = new float[_dimensions];
                for (var d = 0; d < _dimensions; d++) {
                    vector[d] = output[i, d];
                }
                vectors[i] = vector;
            }
        }
        return vectors;
    }

    /// <summary>
    /// SIMD mean pooling — 对 [seqLenDim, dimensions] 的 token 向量按 attention mask 求均值。
    /// <para>使用 Vector&lt;float&gt; 批量累加+除法，比标量快 4-8x（384维/8=48次向量运算 vs 384次标量）。</para>
    /// </summary>
    /// <param name="outputSpan">ONNX 输出的扁平 span（batchSize × seqLenDim × dimensions）。</param>
    /// <param name="attentionMask">attention mask 数组。</param>
    /// <param name="batchIndex">当前处理的 batch 索引。</param>
    /// <param name="seqLenDim">序列长度维度。</param>
    /// <param name="maxSeqLen">最大序列长度（attention mask 步长）。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="vector">输出向量（原地累加+归一化）。</param>
    private static void MeanPoolSimd(
        ReadOnlySpan<float> outputSpan, long[] attentionMask,
        int batchIndex, int seqLenDim, int maxSeqLen, int dimensions,
        float[] vector) {
        var maskSum = 0;
        var batchBase = batchIndex * seqLenDim * dimensions;
        var vecSpan = vector.AsSpan(0, dimensions);
        for (var j = 0; j < seqLenDim; j++) {
            if (attentionMask[batchIndex * maxSeqLen + j] == 0) continue;
            maskSum++;
            var rowOffset = batchBase + j * dimensions;
            VectorAddInPlace(vecSpan, outputSpan.Slice(rowOffset, dimensions));
        }
        if (maskSum > 0) {
            VectorScaleInPlace(vecSpan, 1f / maskSum);
        }
    }

    /// <summary>SIMD 批量累加 target[d] += source[d]，内联到 MeanPoolSimd。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void VectorAddInPlace(Span<float> target, ReadOnlySpan<float> source) {
        var simdWidth = Vector<float>.Count;
        var d = 0;
        for (; d <= target.Length - simdWidth; d += simdWidth) {
            var v = new Vector<float>(target.Slice(d, simdWidth));
            var o = new Vector<float>(source.Slice(d, simdWidth));
            (v + o).CopyTo(target.Slice(d, simdWidth));
        }
        for (; d < target.Length; d++) {
            target[d] += source[d];
        }
    }

    /// <summary>SIMD 批量缩放 target[d] *= scale，内联到 MeanPoolSimd。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void VectorScaleInPlace(Span<float> target, float scale) {
        var simdWidth = Vector<float>.Count;
        var scaleVec = new Vector<float>(scale);
        var d = 0;
        for (; d <= target.Length - simdWidth; d += simdWidth) {
            (new Vector<float>(target.Slice(d, simdWidth)) * scaleVec).CopyTo(target.Slice(d, simdWidth));
        }
        for (; d < target.Length; d++) {
            target[d] *= scale;
        }
    }

    /// <summary>
    /// 标量 mean pooling — DenseTensor 不可用时的 fallback，逐元素累加+除法。
    /// </summary>
    /// <param name="output">ONNX 输出 tensor。</param>
    /// <param name="attentionMask">attention mask 数组。</param>
    /// <param name="batchSize">batch 大小。</param>
    /// <param name="seqLenDim">序列长度维度。</param>
    /// <param name="maxSeqLen">最大序列长度（attention mask 步长）。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="vectors">输出向量数组。</param>
    private static void MeanPoolScalar(
        Tensor<float> output, long[] attentionMask,
        int batchSize, int seqLenDim, int maxSeqLen, int dimensions,
        float[][] vectors) {
        for (var i = 0; i < batchSize; i++) {
            var vector = new float[dimensions];
            var maskSum = 0;
            for (var j = 0; j < seqLenDim; j++) {
                if (attentionMask[i * maxSeqLen + j] == 0) continue;
                maskSum++;
                for (var d = 0; d < dimensions; d++) {
                    vector[d] += output[i, j, d];
                }
            }
            if (maskSum > 0) {
                for (var d = 0; d < dimensions; d++) {
                    vector[d] /= maskSum;
                }
            }
            vectors[i] = vector;
        }
    }

    /// <summary>释放 ONNX session。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _session.Dispose();
    }
}
