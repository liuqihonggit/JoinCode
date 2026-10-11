namespace JoinCode.CodeIndex.Persistence;

/// <summary>
/// 内存索引存储 — 替代 SQLite 持久化(IndexDbContext + Fts5Schema)
/// 全量内存构造,不可变快照 + CAS 无锁并发读写
/// 数据结构: 符号索引(按 fqn/name/file/kind 多维检索) + 调用图 + 依赖图 + 项目依赖 + 文件追踪
/// <para>读：Volatile.Read 无锁 O(1)；写：Interlocked.CompareExchange CAS 原子替换</para>
/// <para>排序索引：FileTrackingKeysSorted/SymbolsSortedByFqn/SymbolsSortedByName 用于 O(log n) 前缀查询</para>
/// <para>反向索引：ProjectRefsByTarget/NuGetRefsByPackage 用于 O(1) 反向查找</para>
/// </summary>
[Register(typeof(InMemoryIndexStore), ServiceLifetime.Singleton)]
public sealed partial class InMemoryIndexStore : ServiceEntity {
    private IndexSnapshot _snapshot = IndexSnapshot.Empty;
    private int _disposed;

    /// <summary>
    /// 获取当前索引快照 — 无锁读取（Volatile.Read 保证可见性）
    /// </summary>
    internal IndexSnapshot GetSnapshot() => Volatile.Read(ref _snapshot);

    /// <summary>
    /// 原子更新索引快照 — CAS 循环，无锁写入
    /// </summary>
    internal void Update(Func<IndexSnapshot, IndexSnapshot> updater) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        while (true) {
            var current = Volatile.Read(ref _snapshot);
            var updated = updater(current);
            if (ReferenceEquals(updated, current)) return;
            if (Interlocked.CompareExchange(ref _snapshot, updated, current) == current) return;
        }
    }

    /// <summary>
    /// 清空所有索引数据 — 原子替换为空快照
    /// </summary>
    public void Clear() {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _snapshot = IndexSnapshot.Empty;
    }

    /// <summary>
    /// 规范化路径键 — 统一路径分隔符用于字典查找
    /// </summary>
    internal static string NormalizeKey(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        base.Dispose();
    }
}

/// <summary>
/// 文件追踪条目 — 替代 file_tracking 表的行
/// </summary>
internal sealed class FileTrackingEntry {
    /// <summary>文件路径 — 规范化后的唯一键</summary>
    public required string FilePath { get; init; }
    /// <summary>文件内容哈希 — 用于判断文件是否变更</summary>
    public required string Hash { get; set; }
    /// <summary>文件中提取的符号数量 — 用于快速统计</summary>
    public required int SymbolCount { get; set; }
    /// <summary>最后一次索引修改时间</summary>
    public required DateTimeOffset LastModified { get; set; }
}
