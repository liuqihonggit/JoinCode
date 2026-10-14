[assembly: InternalsVisibleTo("JoinCode.OnnxEmbedding.Tests")]

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
    /// <param name="modelPath">模型文件路径（.onnx 或 .ort 格式；.ort 自动启用 mmap 零拷贝加载）。</param>
    /// <param name="vocabPath">BERT 词表文件路径（vocab.txt）。</param>
    /// <param name="intraOpNumThreads">ONNX session 内部并行线程数（默认 ProcessorCount，多实例场景传 ProcessorCount/N 避免争抢）。</param>
    public OnnxEmbedder(string modelPath, string vocabPath, int? intraOpNumThreads = null) {
        var threads = intraOpNumThreads is null or <= 0 ? Environment.ProcessorCount : intraOpNumThreads.Value;
        var options = new SessionOptions {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = threads,
        };
        options.AddSessionConfigEntry("session.prepack.enable_parallel", "1");

        var isOrtFormat = modelPath.EndsWith(".ort", StringComparison.OrdinalIgnoreCase);
        if (isOrtFormat) {
            options.AddSessionConfigEntry("session.load_model_format", "ORT");
            options.AddSessionConfigEntry("session.use_memory_mapped_ort_model", "1");
            options.AddSessionConfigEntry("session.use_ort_model_bytes_for_initializers", "1");
        }
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
    /// 批量嵌入文本 → 扁平化向量数组（batchSize × Dimensions 连续存储）。
    /// <para> tokenize → 动态 padding → ONNX 推理 → SIMD mean pooling → 返回扁平向量。</para>
    /// <para>动态 padding: 按 batch 内最长 token 长度向上对齐到 8 的倍数（SIMD 友好），省 30~70% 推理时间。</para>
    /// <para>0-GC mean pooling: 直接在结果 span 上累加，零临时分配。</para>
    /// <para>SIMD: TensorPrimitives 累加+点积+缩放，Vector&lt;long&gt; 批量求 maskSum，乘法代替除法。</para>
    /// </summary>
    /// <param name="texts">待嵌入的文本列表。</param>
    /// <param name="maxSeqLen">最大序列长度上限（默认 32），实际 padding 按 batch 内最长动态计算。</param>
    /// <returns>扁平化向量数组 [batchSize × Dimensions]，第 i 个向量在 [i*Dimensions, (i+1)*Dimensions)。</returns>
    public float[] EmbedBatch(IReadOnlyList<string> texts, int maxSeqLen = 32) {
        var batchSize = texts.Count;

        var allTokens = batchSize >= 8
            ? texts.AsParallel().Select(t => _tokenizer.EncodeToIdsSafe(t)).ToArray()
            : texts.Select(t => _tokenizer.EncodeToIdsSafe(t)).ToArray();
        var maxLen = 0;
        foreach (var t in allTokens) {
            var len = Math.Min(t.Count, maxSeqLen);
            if (len > maxLen) maxLen = len;
        }

        var paddedLen = Math.Max((maxLen + 7) & ~7, 8);

        var pool = ArrayPool<long>.Shared;
        var bufSize = batchSize * paddedLen;
        var inputIds = pool.Rent(bufSize);
        var attentionMask = pool.Rent(bufSize);
        var tokenTypeIds = pool.Rent(bufSize);
        try {
            Array.Clear(inputIds, 0, bufSize);
            Array.Clear(attentionMask, 0, bufSize);
            Array.Clear(tokenTypeIds, 0, bufSize);

            for (var i = 0; i < batchSize; i++) {
                var tokens = allTokens[i];
                var seqLen = Math.Min(tokens.Count, maxSeqLen);
                var rowOffset = i * paddedLen;
                if (tokens is List<int> tokenList) {
                    var tokenSpan = CollectionsMarshal.AsSpan(tokenList);
                    for (var j = 0; j < seqLen; j++) {
                        inputIds[rowOffset + j] = tokenSpan[j];
                        attentionMask[rowOffset + j] = 1;
                    }
                } else {
                    for (var j = 0; j < seqLen; j++) {
                        inputIds[rowOffset + j] = tokens[j];
                        attentionMask[rowOffset + j] = 1;
                    }
                }
            }

            var dims = new[] { batchSize, paddedLen };
            var inputs = _hasTokenTypeIds
                ? new NamedOnnxValue[3]
                : new NamedOnnxValue[2];
            inputs[0] = NamedOnnxValue.CreateFromTensor("input_ids",
                new DenseTensor<long>(inputIds.AsMemory(0, bufSize), dims));
            inputs[1] = NamedOnnxValue.CreateFromTensor("attention_mask",
                new DenseTensor<long>(attentionMask.AsMemory(0, bufSize), dims));
            if (_hasTokenTypeIds) {
                inputs[2] = NamedOnnxValue.CreateFromTensor("token_type_ids",
                    new DenseTensor<long>(tokenTypeIds.AsMemory(0, bufSize), dims));
            }

            using var results = _session.Run(inputs);
            var output = results.First().AsTensor<float>();
            var outputDims = output.Dimensions;
            var dim = _dimensions;
            var vectors = new float[batchSize * dim];

            if (outputDims.Length == 3) {
                var seqLenDim = outputDims[1];
                Debug.Assert(seqLenDim == paddedLen,
                    $"seqLenDim({seqLenDim}) != paddedLen({paddedLen})，attention mask 步长会错位");
                if (output is DenseTensor<float> denseOutput && Vector.IsHardwareAccelerated) {
                    var outputSpan = denseOutput.Buffer.Span;
                    for (var i = 0; i < batchSize; i++) {
                        MeanPoolSimd(outputSpan, attentionMask, i, seqLenDim, paddedLen, dim,
                            vectors.AsSpan(i * dim, dim));
                    }
                } else {
                    MeanPoolScalar(output, attentionMask, batchSize, seqLenDim, paddedLen, dim, vectors);
                }
            } else {
                for (var i = 0; i < batchSize; i++) {
                    var vecOffset = i * dim;
                    for (var d = 0; d < dim; d++) {
                        vectors[vecOffset + d] = output[i, d];
                    }
                }
            }
            return vectors;
        } finally {
            pool.Return(inputIds);
            pool.Return(attentionMask);
            pool.Return(tokenTypeIds);
        }
    }

    /// <summary>
    /// SIMD mean pooling — 对 [seqLenDim, dimensions] 的 token 向量按 attention mask 求均值+L2 归一化。
    /// <para>使用 TensorPrimitives（.NET 8+ AVX2/AVX512 全优化）累加+点积+缩放，比手写 Vector&lt;float&gt; 快 1.5~3x。</para>
    /// <para>融合: mean pooling + L2 归一化数学等价于 v_sum/||v_sum||，maskSum 被约掉，一次缩放 1/norm，用乘法代替除法。</para>
    /// <para>0-GC: 直接写入结果 span，零临时分配。</para>
    /// </summary>
    /// <param name="outputSpan">ONNX 输出的扁平 span（batchSize × seqLenDim × dimensions）。</param>
    /// <param name="attentionMask">attention mask 数组。</param>
    /// <param name="batchIndex">当前处理的 batch 索引。</param>
    /// <param name="seqLenDim">序列长度维度。</param>
    /// <param name="maxSeqLen">最大序列长度（attention mask 步长）。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="vecSpan">输出向量 span（原地累加+归一化，零临时分配）。</param>
    internal static void MeanPoolSimd(
        ReadOnlySpan<float> outputSpan, long[] attentionMask,
        int batchIndex, int seqLenDim, int maxSeqLen, int dimensions,
        Span<float> vecSpan) {
        var maskBase = batchIndex * maxSeqLen;
        var maskSum = SumMaskSimd(attentionMask.AsSpan(maskBase, seqLenDim));
        if (maskSum == 0) return;
        var batchBase = batchIndex * seqLenDim * dimensions;
        for (var j = 0; j < seqLenDim; j++) {
            if (attentionMask[maskBase + j] == 0) continue;
            var rowOffset = batchBase + j * dimensions;
            TensorPrimitives.Add(vecSpan, outputSpan.Slice(rowOffset, dimensions), vecSpan);
        }
        var norm = MathF.Sqrt(TensorPrimitives.Dot(vecSpan, vecSpan));
        if (norm > 0f) {
            TensorPrimitives.Multiply(vecSpan, 1f / norm, vecSpan);
        }
    }

    /// <summary>SIMD 求和 attention mask（0/1 long → 非零个数），Vector&lt;long&gt; 批量累加。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int SumMaskSimd(ReadOnlySpan<long> maskSpan) {
        var sum = 0L;
        var simdWidth = Vector<long>.Count;
        var v = 0;
        for (; v <= maskSpan.Length - simdWidth; v += simdWidth) {
            sum += Vector.Sum(new Vector<long>(maskSpan.Slice(v, simdWidth)));
        }
        for (; v < maskSpan.Length; v++) {
            sum += maskSpan[v];
        }
        return (int)sum;
    }

    /// <summary>
    /// 标量 mean pooling — DenseTensor 不可用时的 fallback，逐元素累加+融合归一化。
    /// <para>融合: v_sum/||v_sum||，maskSum 被约掉，一次缩放 1/norm，用乘法代替除法。</para>
    /// <para>0-GC: 直接写入扁平结果数组，零临时分配。</para>
    /// </summary>
    /// <param name="output">ONNX 输出 tensor。</param>
    /// <param name="attentionMask">attention mask 数组。</param>
    /// <param name="batchSize">batch 大小。</param>
    /// <param name="seqLenDim">序列长度维度。</param>
    /// <param name="maxSeqLen">最大序列长度（attention mask 步长）。</param>
    /// <param name="dimensions">向量维度。</param>
    /// <param name="vectors">扁平化输出向量数组 [batchSize × dimensions]。</param>
    internal static void MeanPoolScalar(
        Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> output, long[] attentionMask,
        int batchSize, int seqLenDim, int maxSeqLen, int dimensions,
        float[] vectors) {
        for (var i = 0; i < batchSize; i++) {
            var vecOffset = i * dimensions;
            var maskSum = 0;
            for (var j = 0; j < seqLenDim; j++) {
                if (attentionMask[i * maxSeqLen + j] == 0) continue;
                maskSum++;
                for (var d = 0; d < dimensions; d++) {
                    vectors[vecOffset + d] += output[i, j, d];
                }
            }
            if (maskSum > 0) {
                var norm = 0f;
                for (var d = 0; d < dimensions; d++) {
                    norm += vectors[vecOffset + d] * vectors[vecOffset + d];
                }
                norm = MathF.Sqrt(norm);
                if (norm > 0f) {
                    var invNorm = 1f / norm;
                    for (var d = 0; d < dimensions; d++) {
                        vectors[vecOffset + d] *= invNorm;
                    }
                }
            }
        }
    }

    /// <summary>释放 ONNX session。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _session.Dispose();
    }

    /// <summary>
    /// 将 ONNX 格式模型转换为 ORT 格式（一次性操作），转换后可用 mmap 零拷贝加载。
    /// </summary>
    /// <param name="onnxModelPath">源 ONNX 模型路径（.onnx）。</param>
    /// <param name="ortModelPath">目标 ORT 模型路径（.ort）。</param>
    /// <returns>转换成功返回 true；目标已存在或转换失败返回 false。</returns>
    public static bool TryConvertToOrtFormat(string onnxModelPath, string ortModelPath) {
        if (File.Exists(ortModelPath)) return false;
        if (!File.Exists(onnxModelPath)) return false;

        var options = new SessionOptions {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            OptimizedModelFilePath = ortModelPath,
        };
        options.AddSessionConfigEntry("session.save_model_format", "ORT");
        using var session = new InferenceSession(onnxModelPath, options);
        return File.Exists(ortModelPath);
    }
}
