namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 嵌入模型抽象 — 对任何语言的代码块一视同仁。
/// <para>实现方可以是 API 调用、ONNX 本地推理、或 simhash 等轻量方案。</para>
/// </summary>
public interface IEmbeddingModel {
    /// <summary>向量维度。</summary>
    int Dimensions { get; }
    /// <summary>模型标识（用于缓存键）。</summary>
    string ModelId { get; }

    /// <summary>嵌入单个文本 → 向量。</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct);

    /// <summary>批量嵌入 — 首次索引提速，减少 API 往返/模型加载开销。</summary>
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct);
}
