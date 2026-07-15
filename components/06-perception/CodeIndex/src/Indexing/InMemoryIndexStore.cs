using JoinCode.Abstractions.Attributes;

namespace JoinCode.CodeIndex.Persistence;

/// <summary>
/// 内存索引存储 — 替代 SQLite 持久化(IndexDbContext + Fts5Schema)
/// 全量内存构造,ReaderWriterLockSlim 保护并发读写
/// 数据结构: 符号索引(按 fqn/name/file/kind 多维检索) + 调用图 + 依赖图 + 项目依赖 + 文件追踪
/// </summary>
[Register]
public sealed class InMemoryIndexStore : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private int _disposed;

    /// <summary>
    /// 符号索引 — 替代 SQLite 的 symbols + symbols_fts 表
    /// </summary>
    internal readonly Dictionary<string, SymbolInfo> SymbolsByFqn = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<SymbolInfo>> SymbolsByName = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<SymbolInfo>> SymbolsByFile = new(StringComparer.Ordinal);
    internal readonly Dictionary<SymbolKind, List<SymbolInfo>> SymbolsByKind = new();

    /// <summary>
    /// 调用图边 — 替代 call_edges 表
    /// </summary>
    internal readonly List<CallEdge> CallEdges = new();
    internal readonly Dictionary<string, List<CallEdge>> CallsByCaller = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<CallEdge>> CallsByCallee = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<CallEdge>> CallsByFile = new(StringComparer.Ordinal);

    /// <summary>
    /// 依赖图边 — 替代 dependency_edges 表
    /// </summary>
    internal readonly List<DependencyEdge> DepEdges = new();
    internal readonly Dictionary<string, List<DependencyEdge>> DepsBySource = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<DependencyEdge>> DepsByTarget = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, List<DependencyEdge>> DepsByFile = new(StringComparer.Ordinal);

    /// <summary>
    /// 项目依赖 — 替代 projects/project_references/nuget_references 表
    /// </summary>
    internal readonly Dictionary<string, ProjectInfo> Projects = new(StringComparer.OrdinalIgnoreCase);
    internal readonly List<ProjectReferenceEdge> ProjectRefs = new();
    internal readonly List<NuGetPackageReference> NuGetRefs = new();

    /// <summary>
    /// 文件追踪 — 替代 file_tracking 表 (用于增量更新判断)
    /// </summary>
    internal readonly Dictionary<string, FileTrackingEntry> FileTracking = new(StringComparer.OrdinalIgnoreCase);

    internal DateTimeOffset LastUpdated = DateTimeOffset.MinValue;

    /// <summary>
    /// 进入写锁 — 所有写操作必须在此 scope 内执行
    /// </summary>
    public IDisposable EnterWriteLock()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return new WriteLockScope(_lock);
    }

    /// <summary>
    /// 进入读锁 — 所有读操作必须在此 scope 内执行
    /// </summary>
    public IDisposable EnterReadLock()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return new ReadLockScope(_lock);
    }

    /// <summary>
    /// 进入可升级读锁 — 用于先读后写的场景
    /// </summary>
    public IDisposable EnterUpgradeableReadLock()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return new UpgradeableReadLockScope(_lock);
    }

    /// <summary>
    /// 清空所有索引数据 — 替代 DELETE FROM 各表
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var scope = EnterWriteLock();
        SymbolsByFqn.Clear();
        SymbolsByName.Clear();
        SymbolsByFile.Clear();
        SymbolsByKind.Clear();
        CallEdges.Clear();
        CallsByCaller.Clear();
        CallsByCallee.Clear();
        CallsByFile.Clear();
        DepEdges.Clear();
        DepsBySource.Clear();
        DepsByTarget.Clear();
        DepsByFile.Clear();
        Projects.Clear();
        ProjectRefs.Clear();
        NuGetRefs.Clear();
        FileTracking.Clear();
        LastUpdated = DateTimeOffset.MinValue;
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
        _lock.Dispose();
    }

    private sealed class WriteLockScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private int _disposed;
        public WriteLockScope(ReaderWriterLockSlim l) { _lock = l; _lock.EnterWriteLock(); }
        public void Dispose()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _lock.ExitWriteLock();
        }
    }

    private sealed class ReadLockScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private int _disposed;
        public ReadLockScope(ReaderWriterLockSlim l) { _lock = l; _lock.EnterReadLock(); }
        public void Dispose()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _lock.ExitReadLock();
        }
    }

    private sealed class UpgradeableReadLockScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _lock;
        private int _disposed;
        public UpgradeableReadLockScope(ReaderWriterLockSlim l) { _lock = l; _lock.EnterUpgradeableReadLock(); }
        public void Dispose()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            _lock.ExitUpgradeableReadLock();
        }
    }
}

/// <summary>
/// 文件追踪条目 — 替代 file_tracking 表的行
/// </summary>
internal sealed class FileTrackingEntry
{
    public required string FilePath { get; init; }
    public required string Hash { get; set; }
    public required int SymbolCount { get; set; }
    public required DateTimeOffset LastModified { get; set; }
}
