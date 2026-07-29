namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 图持久化接口 — 将内存索引序列化到磁盘/从磁盘反序列化
/// 解决 InMemoryIndexStore 进程重启后需重建的问题
/// </summary>
public interface IGraphPersistence
{
    /// <summary>
    /// 将当前索引保存到指定目录
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
