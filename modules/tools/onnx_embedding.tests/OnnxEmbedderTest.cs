namespace OnnxEmbedding.Tests;

/// <summary>
/// OnnxEmbedder 单元测试 — 验证扁平化向量的数值正确性。
/// <para>验证点: ① mean pooling + L2 归一化数学正确性 ② 扁平化向量索引 ③ SIMD maskSum 一致性</para>
/// <para>          ④ 动态 padding ⑤ 返回长度 ⑥ 确定性 ⑦ 区分性 ⑧ L2 归一化</para>
/// </summary>
public class OnnxEmbedderTest {

    // === 模型文件路径（端到端测试用） ===
    private const string ModelPath = @"C:\Users\54076\AppData\Roaming\jcc\embedding\all-MiniLM-L6-v2.onnx";
    private const string VocabPath = @"C:\Users\54076\AppData\Roaming\jcc\embedding\vocab.txt";

    private static readonly Lazy<OnnxEmbedder?> LazyEmbedder = new(() => {
        if (!File.Exists(ModelPath) || !File.Exists(VocabPath)) return null;
        return new OnnxEmbedder(ModelPath, VocabPath, 1);
    });

    private static OnnxEmbedder GetEmbedder() {
        var embedder = LazyEmbedder.Value;
        if (embedder is null) {
            Skip.If(true, "ONNX 模型或词表文件不存在，跳过端到端测试");
            throw new UnreachableException();
        }
        return embedder;
    }

    // ================================================================
    // 验证点③: SumMaskSimd 与标量逐个求和一致
    // ================================================================

    [Fact]
    public void SumMaskSimd_EmptyArray_ReturnsZero() {
        var mask = Array.Empty<long>();
        OnnxEmbedder.SumMaskSimd(mask).Should().Be(0);
    }

    [Fact]
    public void SumMaskSimd_AllZeros_ReturnsZero() {
        var mask = new long[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        OnnxEmbedder.SumMaskSimd(mask).Should().Be(0);
    }

    [Fact]
    public void SumMaskSimd_AllOnes_ReturnsLength() {
        var mask = new long[] { 1, 1, 1, 1, 1, 1, 1, 1 };
        OnnxEmbedder.SumMaskSimd(mask).Should().Be(8);
    }

    [Fact]
    public void SumMaskSimd_SingleElement_One() {
        OnnxEmbedder.SumMaskSimd(new long[] { 1 }).Should().Be(1);
    }

    [Fact]
    public void SumMaskSimd_SingleElement_Zero() {
        OnnxEmbedder.SumMaskSimd(new long[] { 0 }).Should().Be(0);
    }

    [Fact]
    public void SumMaskSimd_MixedMask_MatchesScalarSum() {
        var mask = new long[] { 1, 0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 1 };
        var expected = mask.Sum(x => (int)x);
        OnnxEmbedder.SumMaskSimd(mask).Should().Be(expected);
    }

    [Fact]
    public void SumMaskSimd_LargeArray_MatchesScalarSum() {
        // 1000 元素，验证 SIMD 批量处理 + 尾部标量残段正确
        var rng = new Random(42);
        var mask = new long[1000];
        for (var i = 0; i < mask.Length; i++) mask[i] = rng.Next(2);
        var expected = mask.Sum(x => (int)x);
        OnnxEmbedder.SumMaskSimd(mask).Should().Be(expected);
    }

    [Fact]
    public void SumMaskSimd_LengthNotMultipleOfSimdWidth() {
        // 长度 17，非 Vector<long>.Count 倍数，验证尾部残段
        var mask = new long[17];
        for (var i = 0; i < 17; i++) mask[i] = i % 3 == 0 ? 1 : 0;
        var expected = mask.Sum(x => (int)x);
        OnnxEmbedder.SumMaskSimd(mask).Should().Be(expected);
    }

    // ================================================================
    // 验证点①: MeanPoolSimd 数学正确性（mean pooling + L2 归一化）
    // ================================================================

    [Fact]
    public void MeanPoolSimd_BasicCorrectness_L2NormalizedMean() {
        // 2 tokens, 3 dimensions, padded to 8
        // token0 = [1,2,3], token1 = [4,5,6]
        // v_sum = [5,7,9], norm = sqrt(155) ≈ 12.4499
        // result = v_sum / norm
        const int dimensions = 3;
        const int seqLenDim = 2;
        const int maxSeqLen = 8;
        var attentionMask = new long[] { 1, 1, 0, 0, 0, 0, 0, 0 };
        var outputSpan = new float[] { 1, 2, 3, 4, 5, 6 }.AsSpan();
        var vec = new float[dimensions];

        OnnxEmbedder.MeanPoolSimd(outputSpan, attentionMask, 0, seqLenDim, maxSeqLen, dimensions, vec);

        var expectedNorm = MathF.Sqrt(155f);
        vec[0].Should().BeApproximately(5f / expectedNorm, 1e-5f);
        vec[1].Should().BeApproximately(7f / expectedNorm, 1e-5f);
        vec[2].Should().BeApproximately(9f / expectedNorm, 1e-5f);

        // L2 范数 ≈ 1.0
        var actualNorm = MathF.Sqrt(vec[0] * vec[0] + vec[1] * vec[1] + vec[2] * vec[2]);
        actualNorm.Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void MeanPoolSimd_WithMask_SkipsMaskedTokens() {
        // 3 tokens, 2 dimensions, mask=[1,0,1,...]
        // token0 = [1,0], token1 = [0,1] (masked), token2 = [1,1]
        // v_sum = [1,0] + [1,1] = [2,1], norm = sqrt(5)
        const int dimensions = 2;
        const int seqLenDim = 3;
        const int maxSeqLen = 8;
        var attentionMask = new long[] { 1, 0, 1, 0, 0, 0, 0, 0 };
        var outputSpan = new float[] { 1, 0, 0, 1, 1, 1 }.AsSpan();
        var vec = new float[dimensions];

        OnnxEmbedder.MeanPoolSimd(outputSpan, attentionMask, 0, seqLenDim, maxSeqLen, dimensions, vec);

        var expectedNorm = MathF.Sqrt(5f);
        vec[0].Should().BeApproximately(2f / expectedNorm, 1e-5f);
        vec[1].Should().BeApproximately(1f / expectedNorm, 1e-5f);
    }

    [Fact]
    public void MeanPoolSimd_AllMaskZero_ReturnsZeroVector() {
        const int dimensions = 2;
        const int seqLenDim = 2;
        const int maxSeqLen = 8;
        var attentionMask = new long[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        var outputSpan = new float[] { 1, 2, 3, 4 }.AsSpan();
        var vec = new float[dimensions];

        OnnxEmbedder.MeanPoolSimd(outputSpan, attentionMask, 0, seqLenDim, maxSeqLen, dimensions, vec);

        vec[0].Should().Be(0f);
        vec[1].Should().Be(0f);
    }

    [Fact]
    public void MeanPoolSimd_SingleToken_L2Normalized() {
        // 单 token = [3, 4, 0], norm = 5, result = [0.6, 0.8, 0]
        const int dimensions = 3;
        const int seqLenDim = 1;
        const int maxSeqLen = 8;
        var attentionMask = new long[] { 1, 0, 0, 0, 0, 0, 0, 0 };
        var outputSpan = new float[] { 3, 4, 0 }.AsSpan();
        var vec = new float[dimensions];

        OnnxEmbedder.MeanPoolSimd(outputSpan, attentionMask, 0, seqLenDim, maxSeqLen, dimensions, vec);

        vec[0].Should().BeApproximately(0.6f, 1e-5f);
        vec[1].Should().BeApproximately(0.8f, 1e-5f);
        vec[2].Should().BeApproximately(0f, 1e-5f);
    }

    [Fact]
    public void MeanPoolSimd_BatchIndex1_CorrectOffset() {
        // 2 batches, 2 tokens each, 2 dimensions
        // batch0: token0=[1,0], token1=[0,1] → v_sum=[1,1]
        // batch1: token0=[2,0], token1=[0,2] → v_sum=[2,2]
        const int dimensions = 2;
        const int seqLenDim = 2;
        const int maxSeqLen = 8;
        var attentionMask = new long[] {
            1, 1, 0, 0, 0, 0, 0, 0, // batch 0
            1, 1, 0, 0, 0, 0, 0, 0, // batch 1
        };
        var outputSpan = new float[] {
            1, 0, 0, 1, // batch 0: token0=[1,0], token1=[0,1]
            2, 0, 0, 2, // batch 1: token0=[2,0], token1=[0,2]
        }.AsSpan();
        var vec = new float[dimensions];

        OnnxEmbedder.MeanPoolSimd(outputSpan, attentionMask, 1, seqLenDim, maxSeqLen, dimensions, vec);

        // v_sum = [2,2], norm = sqrt(8), result = [2/sqrt(8), 2/sqrt(8)]
        var expectedNorm = MathF.Sqrt(8f);
        vec[0].Should().BeApproximately(2f / expectedNorm, 1e-5f);
        vec[1].Should().BeApproximately(2f / expectedNorm, 1e-5f);
    }

    // ================================================================
    // 验证点①: MeanPoolScalar 数学正确性（标量 fallback）
    // ================================================================

    [Fact]
    public void MeanPoolScalar_BasicCorrectness_L2NormalizedMean() {
        const int dimensions = 3;
        const int seqLenDim = 2;
        const int maxSeqLen = 8;
        const int batchSize = 1;
        var attentionMask = new long[] { 1, 1, 0, 0, 0, 0, 0, 0 };
        var tensor = new DenseTensor<float>(new float[] { 1, 2, 3, 4, 5, 6 }, new[] { batchSize, seqLenDim, dimensions });
        var vectors = new float[batchSize * dimensions];

        OnnxEmbedder.MeanPoolScalar(tensor, attentionMask, batchSize, seqLenDim, maxSeqLen, dimensions, vectors);

        var expectedNorm = MathF.Sqrt(155f);
        vectors[0].Should().BeApproximately(5f / expectedNorm, 1e-5f);
        vectors[1].Should().BeApproximately(7f / expectedNorm, 1e-5f);
        vectors[2].Should().BeApproximately(9f / expectedNorm, 1e-5f);
    }

    [Fact]
    public void MeanPoolScalar_WithMask_SkipsMaskedTokens() {
        const int dimensions = 2;
        const int seqLenDim = 3;
        const int maxSeqLen = 8;
        const int batchSize = 1;
        var attentionMask = new long[] { 1, 0, 1, 0, 0, 0, 0, 0 };
        var tensor = new DenseTensor<float>(new float[] { 1, 0, 0, 1, 1, 1 }, new[] { batchSize, seqLenDim, dimensions });
        var vectors = new float[batchSize * dimensions];

        OnnxEmbedder.MeanPoolScalar(tensor, attentionMask, batchSize, seqLenDim, maxSeqLen, dimensions, vectors);

        var expectedNorm = MathF.Sqrt(5f);
        vectors[0].Should().BeApproximately(2f / expectedNorm, 1e-5f);
        vectors[1].Should().BeApproximately(1f / expectedNorm, 1e-5f);
    }

    // ================================================================
    // 验证点①: MeanPoolSimd 与 MeanPoolScalar 结果一致
    // ================================================================

    [Fact]
    public void MeanPoolSimd_MatchesMeanPoolScalar() {
        const int dimensions = 4;
        const int seqLenDim = 3;
        const int maxSeqLen = 8;
        const int batchSize = 1;
        var attentionMask = new long[] { 1, 1, 0, 0, 0, 0, 0, 0 };
        var data = new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        // SIMD 路径
        var vecSimd = new float[dimensions];
        OnnxEmbedder.MeanPoolSimd(data.AsSpan(), attentionMask, 0, seqLenDim, maxSeqLen, dimensions, vecSimd);

        // 标量路径
        var tensor = new DenseTensor<float>(data, new[] { batchSize, seqLenDim, dimensions });
        var vecScalar = new float[batchSize * dimensions];
        OnnxEmbedder.MeanPoolScalar(tensor, attentionMask, batchSize, seqLenDim, maxSeqLen, dimensions, vecScalar);

        for (var d = 0; d < dimensions; d++) {
            vecSimd[d].Should().BeApproximately(vecScalar[d], 1e-6f,
                $"SIMD 与标量路径第 {d} 维应一致");
        }
    }

    // ================================================================
    // 验证点④: 动态 padding 正确性（对齐到 8 的倍数）
    // ================================================================

    [Fact]
    public void PaddingFormula_AlignsToMultipleOf8() {
        // paddedLen = Math.Max((maxLen + 7) & ~7, 8)
        var testCases = new (int maxLen, int expected)[] {
            (1, 8), (2, 8), (7, 8), (8, 8),
            (9, 16), (15, 16), (16, 16),
            (17, 24), (24, 24), (25, 32),
            (32, 32), (33, 40),
        };
        foreach (var (maxLen, expected) in testCases) {
            var paddedLen = Math.Max((maxLen + 7) & ~7, 8);
            paddedLen.Should().Be(expected, $"maxLen={maxLen} 应对齐到 {expected}");
            (paddedLen % 8).Should().Be(0, "paddedLen 必须是 8 的倍数");
            paddedLen.Should().BeGreaterThanOrEqualTo(maxLen, "paddedLen 不应小于 maxLen");
            paddedLen.Should().BeGreaterThanOrEqualTo(8, "paddedLen 最小为 8");
        }
    }

    [Fact]
    public void PaddingFormula_MinimumIs8() {
        var paddedLen = Math.Max((0 + 7) & ~7, 8);
        paddedLen.Should().Be(8, "maxLen=0 时 paddedLen 最小为 8");
    }

    // ================================================================
    // 验证点⑤: EmbedBatch 返回扁平 float[] 长度 = batchSize * Dimensions
    // ================================================================

    [SkippableFact]
    public void EmbedBatch_ReturnsFlatArrayWithCorrectLength() {
        var embedder = GetEmbedder();
        var texts = new List<string> { "hello world", "foo bar", "test sentence" };
        var result = embedder.EmbedBatch(texts);
        result.Length.Should().Be(texts.Count * embedder.Dimensions);
    }

    [SkippableFact]
    public void EmbedBatch_SingleText_ReturnsDimensionsLength() {
        var embedder = GetEmbedder();
        var result = embedder.EmbedBatch(new List<string> { "hello" });
        result.Length.Should().Be(embedder.Dimensions);
    }

    // ================================================================
    // 验证点②: 扁平化向量索引正确性 [i*dim, (i+1)*dim)
    // ================================================================

    [SkippableFact]
    public void EmbedBatch_FlatVectorIndexing_MatchesSingleEmbed() {
        var embedder = GetEmbedder();
        var dim = embedder.Dimensions;
        var text1 = "hello world";
        var text2 = "machine learning";

        // 单独嵌入
        var single1 = embedder.EmbedBatch(new List<string> { text1 });
        var single2 = embedder.EmbedBatch(new List<string> { text2 });

        // 批量嵌入
        var batch = embedder.EmbedBatch(new List<string> { text1, text2 });

        batch.Length.Should().Be(2 * dim);

        // 批量中第 0 个向量 [0, dim) 应与单独嵌入一致
        for (var d = 0; d < dim; d++) {
            batch[d].Should().BeApproximately(single1[d], 1e-3f,
                $"batch[0] 第 {d} 维应与单独嵌入 text1 一致");
        }
        // 批量中第 1 个向量 [dim, 2*dim) 应与单独嵌入一致
        for (var d = 0; d < dim; d++) {
            batch[dim + d].Should().BeApproximately(single2[d], 1e-3f,
                $"batch[1] 第 {d} 维应与单独嵌入 text2 一致");
        }
    }

    // ================================================================
    // 验证点⑥: 同一文本多次嵌入结果一致（确定性）
    // ================================================================

    [SkippableFact]
    public void EmbedBatch_Deterministic_SameTextProducesSameVector() {
        var embedder = GetEmbedder();
        var text = "the quick brown fox jumps over the lazy dog";
        var result1 = embedder.EmbedBatch(new List<string> { text });
        var result2 = embedder.EmbedBatch(new List<string> { text });

        result1.Length.Should().Be(result2.Length);
        for (var i = 0; i < result1.Length; i++) {
            result1[i].Should().Be(result2[i], $"第 {i} 维应完全一致（确定性）");
        }
    }

    [SkippableFact]
    public void EmbedBatch_Deterministic_BatchOrderStable() {
        var embedder = GetEmbedder();
        var texts = new List<string> { "alpha", "beta", "gamma" };
        var r1 = embedder.EmbedBatch(texts);
        var r2 = embedder.EmbedBatch(texts);

        for (var i = 0; i < r1.Length; i++) {
            r1[i].Should().Be(r2[i], $"第 {i} 维应完全一致（批量确定性）");
        }
    }

    // ================================================================
    // 验证点⑦: 不同文本产生不同向量
    // ================================================================

    [SkippableFact]
    public void EmbedBatch_DifferentTexts_ProduceDifferentVectors() {
        var embedder = GetEmbedder();
        var dim = embedder.Dimensions;
        var result = embedder.EmbedBatch(new List<string> { "hello world", "goodbye universe" });

        // L1 距离应显著大于 0
        var l1Distance = 0f;
        for (var d = 0; d < dim; d++) {
            l1Distance += MathF.Abs(result[d] - result[dim + d]);
        }
        l1Distance.Should().BeGreaterThan(0.1f, "不同文本应产生显著不同的向量");
    }

    [SkippableFact]
    public void EmbedBatch_SemanticallyDifferentTexts_ProduceDifferentVectors() {
        var embedder = GetEmbedder();
        var dim = embedder.Dimensions;
        var result = embedder.EmbedBatch(new List<string> {
            "the cat sat on the mat",
            "quantum entanglement in physics",
        });

        var l1Distance = 0f;
        for (var d = 0; d < dim; d++) {
            l1Distance += MathF.Abs(result[d] - result[dim + d]);
        }
        l1Distance.Should().BeGreaterThan(0.1f, "语义不同的文本应产生显著不同的向量");
    }

    // ================================================================
    // 验证点⑧: 向量已 L2 归一化（||v|| ≈ 1.0）
    // ================================================================

    [SkippableFact]
    public void EmbedBatch_VectorsAreL2Normalized() {
        var embedder = GetEmbedder();
        var dim = embedder.Dimensions;
        var texts = new List<string> { "hello world", "foo bar", "test sentence" };
        var result = embedder.EmbedBatch(texts);

        for (var i = 0; i < texts.Count; i++) {
            var normSq = 0f;
            for (var d = 0; d < dim; d++) {
                normSq += result[i * dim + d] * result[i * dim + d];
            }
            var norm = MathF.Sqrt(normSq);
            norm.Should().BeApproximately(1f, 1e-4f, $"第 {i} 个向量的 L2 范数应 ≈ 1.0");
        }
    }

    [SkippableFact]
    public void EmbedBatch_SingleVector_L2Normalized() {
        var embedder = GetEmbedder();
        var dim = embedder.Dimensions;
        var result = embedder.EmbedBatch(new List<string> { "a unique sentence for testing" });

        var normSq = 0f;
        for (var d = 0; d < dim; d++) {
            normSq += result[d] * result[d];
        }
        MathF.Sqrt(normSq).Should().BeApproximately(1f, 1e-4f, "单向量 L2 范数应 ≈ 1.0");
    }

    // ================================================================
    // 综合: Dimensions 属性
    // ================================================================

    [SkippableFact]
    public void Dimensions_IsPositive384ForMiniLM() {
        var embedder = GetEmbedder();
        embedder.Dimensions.Should().BePositive();
        embedder.Dimensions.Should().Be(384, "all-MiniLM-L6-v2 模型维度应为 384");
    }
}
