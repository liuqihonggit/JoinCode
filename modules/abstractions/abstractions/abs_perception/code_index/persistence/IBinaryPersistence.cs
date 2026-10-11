namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 统一二进制持久化接口 — 将内存索引序列化到磁盘/从磁盘反序列化
/// 符号索引、向量索引、父文档存储均实现此接口
/// </summary>
public interface IBinaryPersistence {
    /// <summary>
    /// 将当前索引保存到指定目录（二进制格式）
    /// </summary>
    Task SaveAsync(string directory, CancellationToken ct);

    /// <summary>
    /// 从指定目录加载索引(若存在且版本匹配)
    /// </summary>
    Task<bool> LoadAsync(string directory, CancellationToken ct);

    /// <summary>
    /// 检查指定目录是否存在有效的持久化索引
    /// </summary>
    Task<bool> ExistsAsync(string directory, CancellationToken ct);
}
