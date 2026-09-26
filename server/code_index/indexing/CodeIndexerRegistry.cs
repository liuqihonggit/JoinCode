namespace JoinCode.CodeIndex;

/// <summary>
/// 代码索引仓库注册表实现 — 管理多个仓库的 ICodeIndexer 实例
/// 每个仓库拥有独立的 InMemoryIndexStore + CodeIndexer
/// 通过 RepoRegistered/RepoUnregistered 事件通知订阅方（如 FileWatcherIntegrationRegistry）
/// 基于 MapRegistry 不可变字典 + 无锁 CAS，消除 ReaderWriterLockSlim 死锁风险
/// </summary>
[Register(typeof(ICodeIndexerRegistry), ServiceLifetime.Singleton)]
public sealed class CodeIndexerRegistry : ServiceEntity, ICodeIndexerRegistry, IDisposable {
    private readonly IFileSystem _fs;
    private readonly ICodeIndexer _defaultIndexer;
    private readonly MapRegistry<string, RegisteredRepo> _repos = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>
    /// 构造代码索引仓库注册表
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="defaultIndexer">默认索引器实例</param>
    public CodeIndexerRegistry(IFileSystem fs, ICodeIndexer defaultIndexer) {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(defaultIndexer);
        _fs = fs;
        _defaultIndexer = defaultIndexer;
    }

    /// <summary>默认索引器实例</summary>
    public ICodeIndexer? DefaultIndexer => _defaultIndexer;

    /// <summary>仓库注册成功时触发 — 携带仓库标识、工作区根和索引器实例</summary>
    public event EventHandler<RepoRegisteredEventArgs>? RepoRegistered;

    /// <summary>仓库注销成功时触发 — 携带仓库标识</summary>
    public event EventHandler<RepoUnregisteredEventArgs>? RepoUnregistered;

    /// <summary>
    /// 注册新仓库 — 创建独立的 InMemoryIndexStore + CodeIndexer 并触发 RepoRegistered 事件
    /// </summary>
    /// <param name="repoId">仓库标识</param>
    /// <param name="workspaceRoot">工作区根路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>仓库注册信息</returns>
    public async Task<RepoRegistration> RegisterAsync(string repoId, string workspaceRoot, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var store = new InMemoryIndexStore();
        var concreteIndexer = new CodeIndexer(store, _fs);
        var registration = new RepoRegistration {
            RepoId = repoId,
            WorkspaceRoot = workspaceRoot,
            RegisteredAt = DateTimeOffset.UtcNow,
            IsDefault = false,
            IsWatching = false,
        };

        if (!_repos.TryAdd(repoId, new RegisteredRepo(registration, store, concreteIndexer))) {
            concreteIndexer.DisposeSafe();
            await store.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"Repository '{repoId}' is already registered.");
        }

        RepoRegistered?.Invoke(this, new RepoRegisteredEventArgs {
            RepoId = repoId,
            WorkspaceRoot = workspaceRoot,
            Indexer = concreteIndexer,
        });

        return registration;
    }

    /// <summary>
    /// 注销仓库 — 释放索引器和存储，并触发 RepoUnregistered 事件
    /// </summary>
    /// <param name="repoId">仓库标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>注销成功返回 true，仓库不存在返回 false</returns>
    public async Task<bool> UnregisterAsync(string repoId, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(repoId);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (!_repos.TryRemove(repoId, out var repo))
            return false;

        await repo.Indexer.DisposeAsync().ConfigureAwait(false);
        await repo.Store.DisposeAsync().ConfigureAwait(false);

        RepoUnregistered?.Invoke(this, new RepoUnregisteredEventArgs {
            RepoId = repoId,
        });

        return true;
    }

    /// <summary>
    /// 列出所有已注册仓库 — 包含默认仓库和所有动态注册的仓库
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>仓库注册信息只读列表</returns>
    public Task<IReadOnlyList<RepoRegistration>> ListReposAsync(CancellationToken ct) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var list = new List<RepoRegistration>();

        if (_defaultIndexer is not null) {
            list.Add(new RepoRegistration {
                RepoId = "default",
                WorkspaceRoot = "",
                RegisteredAt = DateTimeOffset.MinValue,
                IsDefault = true,
                IsWatching = false,
            });
        }

        list.AddRange(_repos.GetAll().Select(r => r.Registration));

        return Task.FromResult<IReadOnlyList<RepoRegistration>>(list);
    }

    /// <summary>
    /// 根据仓库标识获取索引器 — "default" 返回默认索引器
    /// </summary>
    /// <param name="repoId">仓库标识</param>
    /// <returns>索引器实例，不存在时返回 null</returns>
    public ICodeIndexer? GetIndexer(string repoId) {
        ArgumentNullException.ThrowIfNull(repoId);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (string.Equals(repoId, "default", StringComparison.OrdinalIgnoreCase))
            return _defaultIndexer;

        return _repos.TryGetValue(repoId, out var repo) ? repo.Indexer : null;
    }

    /// <summary>
    /// 释放资源 — 释放所有仓库的索引器和存储
    /// </summary>
    public override void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        foreach (var repo in _repos.GetAll()) {
            repo.Indexer.DisposeSafe();
            repo.Store.Dispose();
        }
        _repos.Clear();

        base.Dispose();
    }

    private sealed record RegisteredRepo(RepoRegistration Registration, InMemoryIndexStore Store, CodeIndexer Indexer);
}
