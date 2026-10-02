namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 通用键值存储接口 — LSM-Tree 持久化抽象。
/// <para>生产环境: PithosKvStore (委托给 PithosDB LSM-tree, WAL+MemTable+SSTable+Compaction)</para>
/// <para>测试环境: InMemoryKvStore (纯内存, 0磁盘IO)</para>
/// <para>所有操作线程安全,支持并发读 + 串行写。</para>
/// </summary>
public interface IKvStore : IAsyncDisposable {
    /// <summary>
    /// 写入键值对 — 写入 WAL + MemTable, 达到阈值后 flush 到 SSTable。
    /// </summary>
    /// <param name="key">键(UTF-8 字节)</param>
    /// <param name="value">值(任意字节)</param>
    /// <param name="ct">取消令牌</param>
    ValueTask PutAsync(byte[] key, byte[] value, CancellationToken ct = default);

    /// <summary>
    /// 读取键值对 — 查找顺序: MemTable → L0 SSTable → L1 → ... → Ln。
    /// </summary>
    /// <returns>值字节, 不存在返回 null</returns>
    ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken ct = default);

    /// <summary>
    /// 删除键值对 — 写入墓碑标记(tombstone), 压实时物理删除。
    /// </summary>
    ValueTask DeleteAsync(byte[] key, CancellationToken ct = default);

    /// <summary>
    /// 范围扫描 [from, to] — k-way merge 跨 MemTable + 所有 SSTable 层级, 排序去重输出。
    /// </summary>
    /// <param name="from">起始键(含), null 表示从头开始</param>
    /// <param name="to">结束键(含), null 表示到末尾</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>排序的 (Key, Value) 对</returns>
    IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? from = null,
        byte[]? to = null,
        CancellationToken ct = default);
}
