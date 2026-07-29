namespace JoinCode.CodeIndex;

/// <summary>
/// 代码索引仓库注册表实现 — 管理多个仓库的 ICodeIndexer 实例
/// 每个仓库拥有独立的 InMemoryIndexStore + CodeIndexer
/// 通过 RepoRegistered/RepoUnregistered 事件通知订阅方（如 FileWatcherIntegrationRegistry）
/// </summary>
[Register(typeof(ICodeIndexerRegistry))]
public sealed class CodeIndexerRegistry : ICodeIndexerRegistry, IDisposable
{
    private readonly IFileSystem _fs;
    private readonly ICodeIndexer _defaultIndexer;
    private readonly Dictionary<string, RegisteredRepo> _repos = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim _lock = new();
    private int _disposed;

    public CodeIndexerRegistry(IFileSystem fs, ICodeIndexer defaultIndexer)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(defaultIndexer);
        _fs = fs;
        _defaultIndexer = defaultIndexer;
    }

    public ICodeIndexer? DefaultIndexer => _defaultIndexer;

    public event EventHandler<RepoRegisteredEventArgs>? RepoRegistered;
    public event EventHandler<RepoUnregisteredEventArgs>? RepoUnregistered;

    public Task<RepoRegistration> RegisterAsync(string repoId, string workspaceRoot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repoId);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        RepoRegistration registration;
        CodeIndexer concreteIndexer;

        _lock.EnterWriteLock();
        try
        {
            if (_repos.ContainsKey(repoId))
                throw new InvalidOperationException($"Repository '{repoId}' is already registered.");

            var store = new InMemoryIndexStore();
            concreteIndexer = new CodeIndexer(store, _fs);
            registration = new RepoRegistration
            {
                RepoId = repoId,
                WorkspaceRoot = workspaceRoot,
                RegisteredAt = DateTimeOffset.UtcNow,
                IsDefault = false,
                IsWatching = false,
            };

            _repos[repoId] = new RegisteredRepo(registration, store, concreteIndexer);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        RepoRegistered?.Invoke(this, new RepoRegisteredEventArgs
        {
            RepoId = repoId,
            WorkspaceRoot = workspaceRoot,
            Indexer = concreteIndexer,
        });

        return Task.FromResult(registration);
    }

    public Task<bool> UnregisterAsync(string repoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repoId);

        _lock.EnterWriteLock();
        try
        {
            if (!_repos.Remove(repoId, out var repo))
                return Task.FromResult(false);

            repo.Indexer.Dispose();
            repo.Store.Dispose();
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        RepoUnregistered?.Invoke(this, new RepoUnregisteredEventArgs
        {
            RepoId = repoId,
        });

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<RepoRegistration>> ListReposAsync(CancellationToken ct)
    {
        _lock.EnterReadLock();
        try
        {
            var list = new List<RepoRegistration>();

            if (_defaultIndexer is not null)
            {
                list.Add(new RepoRegistration
                {
                    RepoId = "default",
                    WorkspaceRoot = "",
                    RegisteredAt = DateTimeOffset.MinValue,
                    IsDefault = true,
                    IsWatching = false,
                });
            }

            list.AddRange(_repos.Values.Select(r => r.Registration));

            return Task.FromResult<IReadOnlyList<RepoRegistration>>(list);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public ICodeIndexer? GetIndexer(string repoId)
    {
        ArgumentNullException.ThrowIfNull(repoId);

        if (string.Equals(repoId, "default", StringComparison.OrdinalIgnoreCase))
            return _defaultIndexer;

        _lock.EnterReadLock();
        try
        {
            return _repos.TryGetValue(repoId, out var repo) ? repo.Indexer : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _lock.EnterWriteLock();
        try
        {
            foreach (var repo in _repos.Values)
            {
                repo.Indexer.Dispose();
                repo.Store.Dispose();
            }
            _repos.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        _lock.Dispose();
    }

    private sealed record RegisteredRepo(RepoRegistration Registration, InMemoryIndexStore Store, CodeIndexer Indexer);
}
