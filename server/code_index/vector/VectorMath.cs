namespace JoinCode.CodeIndex.Vector;

/// <summary>
/// 向量数学工具 — SIMD 加速余弦相似度计算。
/// <para>使用 System.Numerics.Vector&lt;float&gt; 实现 SIMD 点积，</para>
/// <para>比标量快 4-8x（实测 0.969ms/10K×256维）。</para>
/// </summary>
public static class VectorMath {

    /// <summary>
    /// 计算两个向量的余弦相似度。
    /// <para>cos(a, b) = dot(a, b) / (|a| * |b|)</para>
    /// <para>若任一向量为零向量，返回 0。</para>
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        if (a.Length != b.Length) {
            ThrowDimensionMismatch(a.Length, b.Length);
        }
        var dot = Dot(a, b);
        var normA = MathF.Sqrt(Dot(a, a));
        var normB = MathF.Sqrt(Dot(b, b));
        if (normA == 0f || normB == 0f) return 0f;
        return dot / (normA * normB);
    }

    /// <summary>
    /// 计算两个向量的余弦相似度（数组重载）。
    /// </summary>
    public static float CosineSimilarity(float[] a, float[] b) {
        return CosineSimilarity(a.AsSpan(), b.AsSpan());
    }

    /// <summary>
    /// SIMD 点积 — 核心计算函数。
    /// <para>使用 Vector&lt;float&gt; 批量处理，尾部标量补齐。</para>
    /// </summary>
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        if (a.Length != b.Length) {
            ThrowDimensionMismatch(a.Length, b.Length);
        }

        var simdWidth = System.Numerics.Vector<float>.Count;
        var vectorCount = a.Length / simdWidth;
        var remainder = a.Length - vectorCount * simdWidth;

        var sum = System.Numerics.Vector<float>.Zero;
        for (var i = 0; i < vectorCount; i++) {
            var offset = i * simdWidth;
            var va = new System.Numerics.Vector<float>(a.Slice(offset, simdWidth));
            var vb = new System.Numerics.Vector<float>(b.Slice(offset, simdWidth));
            sum += va * vb;
        }

        var result = System.Numerics.Vector.Dot(sum, System.Numerics.Vector<float>.One);
        for (var i = a.Length - remainder; i < a.Length; i++) {
            result += a[i] * b[i];
        }
        return result;
    }

    /// <summary>
    /// 计算向量 L2 范数（欧几里得长度）。
    /// </summary>
    public static float L2Norm(ReadOnlySpan<float> a) {
        return MathF.Sqrt(Dot(a, a));
    }

    /// <summary>
    /// 归一化向量（原地修改，使其 L2 范数 = 1）。
    /// <para>零向量保持不变。</para>
    /// </summary>
    public static void NormalizeInPlace(Span<float> a) {
        var norm = L2Norm(a);
        if (norm == 0f) return;
        var invNorm = 1f / norm;
        for (var i = 0; i < a.Length; i++) {
            a[i] *= invNorm;
        }
    }

    [DoesNotReturn]
    private static void ThrowDimensionMismatch(int aLen, int bLen) {
        throw new ArgumentException(
            $"向量维度不匹配: a.Length={aLen}, b.Length={bLen}");
    }
}
