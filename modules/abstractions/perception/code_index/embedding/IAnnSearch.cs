namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 近似最近邻搜索抽象 — 语言无关、嵌入模型无关。
/// <para>起步方案用暴力搜索 + SIMD（BruteForceAnn），后续可替换为 HNSW 等。</para>
/// </summary>
public interface IAnnSearch {
    /// <summary>添加单个向量。</summary>
    void Add(string id, ReadOnlySpan<float> vector);
    /// <summary>批量添加向量。</summary>
    void AddRange(IReadOnlyList<AnnVectorInput> items);
    /// <summary>删除向量。</summary>
    void Remove(string id);
    /// <summary>搜索 top-K 最相似向量（余弦相似度）。</summary>
    IReadOnlyList<AnnSearchHit> Search(float[] query, int topK, CancellationToken ct);
    /// <summary>当前向量数量。</summary>
    int Count { get; }
}

/// <summary>
/// 批量写入 ANN 的单条向量 — 标识与向量本体。
/// </summary>
/// <param name="Id">向量标识。</param>
/// <param name="Vector">向量数据。</param>
public sealed record AnnVectorInput(string Id, float[] Vector);

/// <summary>
/// ANN 搜索命中 — 标识与余弦相似度得分。
/// </summary>
/// <param name="Id">命中的向量标识。</param>
/// <param name="Score">相似度得分（越大越相似）。</param>
public sealed record AnnSearchHit(string Id, float Score);
