namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 近似最近邻搜索抽象 — 语言无关、嵌入模型无关。
/// <para>起步方案用暴力搜索 + SIMD（BruteForceAnn），后续可替换为 HNSW 等。</para>
/// </summary>
public interface IAnnSearch {
    /// <summary>添加单个向量。</summary>
    void Add(string id, float[] vector);
    /// <summary>批量添加向量。</summary>
    void AddRange(IReadOnlyList<(string Id, float[] Vector)> items);
    /// <summary>删除向量。</summary>
    void Remove(string id);
    /// <summary>搜索 top-K 最相似向量（余弦相似度）。</summary>
    IReadOnlyList<(string Id, float Score)> Search(float[] query, int topK, CancellationToken ct);
    /// <summary>当前向量数量。</summary>
    int Count { get; }
}
