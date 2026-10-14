namespace CodeIndex.E2E.Tests;

/// <summary>
/// OnnxEmbedder 回归测试 — 验证 tokenizer 正确分词，不同文本产生不同向量。
/// <para>复现 bug: considerPreTokenization=false 导致所有文本被编码为 [CLS][UNK][SEP]，</para>
/// <para>所有向量相同，搜索 Score 全 1.0000。</para>
/// <para>EmbedBatch 返回扁平化 float[] [batchSize × Dimensions]，第 i 个向量在 [i*dim, (i+1)*dim)。</para>
/// </summary>
public sealed class OnnxEmbedderRegressionTest {

    private static string GetModelPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jcc", "embedding", "model_quantized.onnx");

    private static string GetVocabPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jcc", "embedding", "vocab.txt");

    private static bool IsModelAvailable() => File.Exists(GetModelPath()) && File.Exists(GetVocabPath());

    [Fact]
    public void DifferentTexts_ProduceDifferentVectors() {
        if (!IsModelAvailable()) return;

        using var embedder = new OnnxEmbedder(GetModelPath(), GetVocabPath());
        var dim = embedder.Dimensions;
        var flat = embedder.EmbedBatch(["hello world", "public class Foo {}"], 32);

        var sim = CosineSimilarity(flat.AsSpan(0, dim), flat.AsSpan(dim, dim));
        Assert.True(sim < 0.99f, $"不同文本的余弦相似度应 < 0.99，实际 = {sim:F6}（可能 considerPreTokenization 又被改为 false）");
    }

    [Fact]
    public void SameText_ProducesIdenticalVectors() {
        if (!IsModelAvailable()) return;

        using var embedder = new OnnxEmbedder(GetModelPath(), GetVocabPath());
        var dim = embedder.Dimensions;
        var flat = embedder.EmbedBatch(["hello world", "hello world"], 32);

        var sim = CosineSimilarity(flat.AsSpan(0, dim), flat.AsSpan(dim, dim));
        Assert.True(sim > 0.999f, $"相同文本的余弦相似度应 > 0.999，实际 = {sim:F6}");
    }

    [Fact]
    public void NonEmptyText_VectorIsNonZero() {
        if (!IsModelAvailable()) return;

        using var embedder = new OnnxEmbedder(GetModelPath(), GetVocabPath());
        var dim = embedder.Dimensions;
        var vec = embedder.EmbedBatch(["hello world"], 32).AsSpan(0, dim);

        var normSquared = 0f;
        for (var i = 0; i < vec.Length; i++) normSquared += vec[i] * vec[i];
        var norm = MathF.Sqrt(normSquared);
        Assert.True(norm > 0.1f, $"非空文本的向量范数应 > 0.1，实际 = {norm:F6}");
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b) {
        var dot = 0f;
        var normA = 0f;
        var normB = 0f;
        for (var i = 0; i < a.Length; i++) {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0f || normB == 0f) return 0f;
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
