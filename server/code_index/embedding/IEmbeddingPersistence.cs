namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// 嵌入索引持久化后端 — 多态接口。
/// <para>bin 实现：mmap 二进制批量序列化，未来可扩展增量。</para>
/// <para>LSM 实现：PithosDB LSM-Tree 原生增量持久化。</para>
/// </summary>
internal interface IEmbeddingPersistence : IAsyncDisposable {
    /// <summary>检查指定目录是否存在持久化数据。</summary>
    Task<bool> ExistsAsync(string dirPath, CancellationToken ct);

    /// <summary>全量保存快照到指定目录。</summary>
    Task SaveAsync(string dirPath, EmbeddingSnapshot snapshot, CancellationToken ct);

    /// <summary>从指定目录加载快照。返回 null 表示不存在或加载失败。</summary>
    Task<EmbeddingSnapshot?> LoadAsync(string dirPath, CancellationToken ct);
}

/// <summary>
/// 嵌入索引快照 — 持久化数据载体，与后端实现无关。
/// </summary>
internal sealed record EmbeddingSnapshot {
    /// <summary>向量维度。</summary>
    public required int Dims { get; init; }
    /// <summary>所有块的三元组（chunkId, 向量, 元数据, 哈希）。</summary>
    public required IReadOnlyList<(string ChunkId, float[] Vector, ChunkMetadata Meta, string Hash)> Chunks { get; init; }
    /// <summary>ANN 图段字节（null=无图）。</summary>
    public byte[]? GraphBytes { get; init; }
}
