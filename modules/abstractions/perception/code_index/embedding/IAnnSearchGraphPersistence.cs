namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// ANN 图结构持久化接口 — 支持图结构持久化的 ANN 实现此接口。
/// <para>HNSW 等图结构 ANN 实现此接口，加载时直接恢复邻接表，不重新构图。</para>
/// <para>BruteForceAnn 等无图结构的 ANN 不实现此接口，加载时逐个 Add。</para>
/// <para>向量数据由 EmbeddingIndex 统一持久化，此接口只存/取图结构（邻接表+入口点）。</para>
/// </summary>
public interface IAnnSearchGraphPersistence {

    /// <summary>
    /// 保存图结构到 BinaryWriter — 邻接表+入口点+层数。
    /// <para>在 EmbeddingIndex.SaveAsync 中，向量数据写入后调用此方法追加图数据。</para>
    /// </summary>
    void SaveGraph(BinaryWriter bw);

    /// <summary>
    /// 从 BinaryReader 加载图结构，并用 vectors 填充内部向量存储。
    /// <para>在 EmbeddingIndex.LoadAsync 中，先读取向量到字典，再调用此方法恢复图。</para>
    /// <para>加载后无需重新构图，直接恢复邻接表即可。</para>
    /// </summary>
    /// <param name="br">二进制读取器（指向图数据起始位置）。</param>
    /// <param name="vectors">所有向量数据（id → vector），用于填充内部存储。</param>
    void LoadGraph(BinaryReader br, IReadOnlyDictionary<string, float[]> vectors);
}
