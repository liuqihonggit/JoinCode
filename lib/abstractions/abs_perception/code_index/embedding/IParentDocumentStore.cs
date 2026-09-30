namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 父文档存储抽象 — 父文档检索的完整上下文存储。
/// <para>向量库存小块（方法/属性），召回小块后通过 ParentChunkId 查此存储取父文档原文。</para>
/// <para>纯内存实现（InMemoryParentDocumentStore），进程退出释放，下次重建。</para>
/// </summary>
public interface IParentDocumentStore : IIndexStore {
    /// <summary>添加单个父文档。</summary>
    void Add(ParentDocument document);
    /// <summary>批量添加父文档。</summary>
    void AddRange(IReadOnlyList<ParentDocument> documents);
    /// <summary>按 ChunkId 获取父文档 — 不存在返回 null。</summary>
    ParentDocument? Get(string chunkId);
    /// <summary>删除单个父文档。</summary>
    void Remove(string chunkId);
    /// <summary>删除文件关联的所有父文档。</summary>
    void RemoveFile(string filePath);
    /// <summary>清空所有父文档。</summary>
    void Clear();
}
