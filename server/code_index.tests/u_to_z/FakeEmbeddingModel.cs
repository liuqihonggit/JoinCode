namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// 假嵌入模型 — 用于测试，将文本哈希映射为确定性向量。
/// </summary>
internal sealed class FakeEmbeddingModel : IEmbeddingModel {
    public int Dimensions { get; }
    public string ModelId => "fake";

    public FakeEmbeddingModel(int dimensions = 8) {
        Dimensions = dimensions;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct) {
        return Task.FromResult(Embed(text));
    }

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) {
        var results = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++) {
            results[i] = Embed(texts[i]);
        }
        return Task.FromResult(results);
    }

    private float[] Embed(string text) {
        var vector = new float[Dimensions];
        if (string.IsNullOrEmpty(text)) return vector;
        foreach (var c in text) {
            var idx = ((c % Dimensions) + Dimensions) % Dimensions;
            vector[idx] += 1f;
        }
        return vector;
    }
}
