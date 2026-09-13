namespace JoinCode.CodeIndex.Persistence;

/// <summary>
/// 内存索引存储 — 替代 SQLite 持久化(IndexDbContext + Fts5Schema)
/// 全量内存构造,ReaderWriterLockSlim 保护并发读写
/// 数据结构: 符号索引(按 fqn/name/file/kind 多维检索) + 调用图 + 依赖图 + 项目依赖 + 文件追踪
/// </summary>
[Register(typeof(InMemoryIndexStore), ServiceLifetime.Singleton)]
public sealed partial class InMemoryIndexStore : ServiceEntity, IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private int _disposed;

    /// <summary>
    /// 符号索引 — 替代 SQLite 的 symbols + symbols_fts 表
    /// </summary>
    internal readonly Dictionary<string, SymbolInfo> SymbolsByFqn = new(StringComparer.Ordinal);
    /// <summary>按符号名索引 — 支持同名符号多重匹配检索</summary>
    internal readonly Dictionary<string, List<SymbolInfo>> SymbolsByName = new(StringComparer.Ordinal);
    /// <summary>按文件路径索引 — 支持按文件检索其包含的全部符号</summary>
    internal readonly Dictionary<string, List<SymbolInfo>> SymbolsByFile = new(StringComparer.Ordinal);
    /// <summary>按符号种类索引 — 支持按 Class/Method/Property 等类别检索</summary>
    internal readonly Dictionary<SymbolKind, List<SymbolInfo>> SymbolsByKind = new();

    /// <summary>
    /// 调用图边 — 替代 call_edges 表
    /// </summary>
    internal readonly List<CallEdge> CallEdges = new();
    /// <summary>按调用方符号索引 — 支持查询某符号调用了哪些其他符号</summary>
    internal readonly Dictionary<string, List<CallEdge>> CallsByCaller = new(StringComparer.Ordinal);
    /// <summary>按被调用方符号索引 — 支持查询某符号被哪些符号调用</summary>
    internal readonly Dictionary<string, List<CallEdge>> CallsByCallee = new(StringComparer.Ordinal);
    /// <summary>按调用点文件索引 — 支持按文件检索其包含的全部调用边</summary>
    internal readonly Dictionary<string, List<CallEdge>> CallsByFile = new(StringComparer.Ordinal);

    /// <summary>
    /// 依赖图边 — 替代 dependency_edges 表
    /// </summary>
    internal readonly List<DependencyEdge> DepEdges = new();
    /// <summary>按依赖源符号索引 — 支持查询某符号依赖了哪些其他符号</summary>
    internal readonly Dictionary<string, List<DependencyEdge>> DepsBySource = new(StringComparer.Ordinal);
    /// <summary>按依赖目标符号索引 — 支持查询某符号被哪些符号依赖</summary>
    internal readonly Dictionary<string, List<DependencyEdge>> DepsByTarget = new(StringComparer.Ordinal);
    /// <summary>按依赖源文件索引 — 支持按文件检索其包含的全部依赖边</summary>
    internal readonly Dictionary<string, List<DependencyEdge>> DepsByFile = new(StringComparer.Ordinal);

    /// <summary>
    /// 项目依赖 — 替代 projects/project_references/nuget_references 表
    /// </summary>
    internal readonly Dictionary<string, ProjectInfo> Projects = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>项目引用边 — 按源项目路径索引,记录项目间引用关系</summary>
    internal readonly Dictionary<string, List<ProjectReferenceEdge>> ProjectRefs = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>NuGet 包引用 — 按项目路径索引,记录每个项目的 NuGet 依赖</summary>
    internal readonly Dictionary<string, List<NuGetPackageReference>> NuGetRefs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 规范化路径键 — 统一路径分隔符用于字典查找
    /// </summary>
    internal static string NormalizeKey(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    /// <summary>
    /// 文件追踪 — 替代 file_tracking 表 (用于增量更新判断)
    /// </summary>
    internal readonly Dictionary<string, FileTrackingEntry> FileTracking = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>索引最后更新时间 — 用于判断索引新鲜度</summary>
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

    /// <summary>
    /// 释放内部读写锁资源 — 派生类可重写以追加自定义释放逻辑
    /// </summary>
    protected override void OnDispose()
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
    /// <summary>文件路径 — 规范化后的唯一键</summary>
    public required string FilePath { get; init; }
    /// <summary>文件内容哈希 — 用于判断文件是否变更</summary>
    public required string Hash { get; set; }
    /// <summary>文件中提取的符号数量 — 用于快速统计</summary>
    public required int SymbolCount { get; set; }
    /// <summary>最后一次索引修改时间</summary>
    public required DateTimeOffset LastModified { get; set; }
}
