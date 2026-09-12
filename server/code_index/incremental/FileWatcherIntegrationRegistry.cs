namespace JoinCode.CodeIndex;

/// <summary>
/// 多仓库文件监听注册表 — 订阅 ICodeIndexerRegistry 的 RepoRegistered/RepoUnregistered 事件
/// 按 repo_id 隔离管理 FileWatcherIntegration 实例
/// 注册仓库时自动启动 watcher，注销时自动停止
/// </summary>
[Register(typeof(FileWatcherIntegrationRegistry), ServiceLifetime.Singleton)]
public sealed class FileWatcherIntegrationRegistry : IAsyncDisposable
{
    private readonly ICodeIndexerRegistry _registry;
    private readonly IFileSystem _fs;
    private readonly Dictionary<string, FileWatcherIntegration> _watchers = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly ILogger<FileWatcherIntegrationRegistry>? _logger;
    private int _disposed;

    public FileWatcherIntegrationRegistry(ICodeIndexerRegistry registry, IFileSystem fs, ILogger<FileWatcherIntegrationRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(fs);
        _registry = registry;
        _fs = fs;
        _logger = logger;

        _registry.RepoRegistered += OnRepoRegistered;
        _registry.RepoUnregistered += OnRepoUnregistered;
    }

    private void OnRepoRegistered(object? sender, RepoRegisteredEventArgs e)
    {
        if (_disposed != 0) return;

        var watcher = new FileWatcherIntegration(e.Indexer, e.WorkspaceRoot, _fs, onError: null);

        using (_lock.EnterWriteScope())
        {
            _watchers[e.RepoId] = watcher;
        }

        _ = watcher.StartAsync(CancellationToken.None);
    }

    private void OnRepoUnregistered(object? sender, RepoUnregisteredEventArgs e)
    {
        if (_disposed != 0) return;

        FileWatcherIntegration? watcher;

        using (_lock.EnterWriteScope())
        {
            _watchers.Remove(e.RepoId, out watcher);
        }

        if (watcher is not null)
        {
            var capturedWatcher = watcher;
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var w = capturedWatcher;
                    await w.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "FileWatcherIntegrationRegistry: 停止 watcher 失败");
                }
            });
        }
    }

    /// <summary>
    /// 获取指定仓库的 watcher 是否正在运行
    /// </summary>
    public bool IsWatching(string repoId)
    {
        if (_disposed != 0) return false;

        using var scope = _lock.EnterReadScope();
        return _watchers.ContainsKey(repoId);
    }

    /// <summary>
    /// 获取所有正在监听的仓库 ID（遍历器，不分配新集合）
    /// </summary>
    public IEnumerable<string> GetWatchingRepoIds()
    {
        if (_disposed != 0) return [];

        using var scope = _lock.EnterReadScope();
        return _watchers.Keys.ToList();
    }

    private async Task StopAndDisposeWatcherAsync(FileWatcherIntegration watcher)
    {
        try
        {
            await watcher.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await watcher.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "FileWatcherIntegrationRegistry: 停止 watcher 失败");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _registry.RepoRegistered -= OnRepoRegistered;
        _registry.RepoUnregistered -= OnRepoUnregistered;

        List<FileWatcherIntegration> watchers;
        using (_lock.EnterWriteScope())
        {
            watchers = [.. _watchers.Values];
            _watchers.Clear();
        }

        foreach (var watcher in watchers)
        {
            try
            {
                await watcher.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await watcher.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "FileWatcherIntegrationRegistry: 释放 watcher 失败");
            }
        }

        _lock.Dispose();
    }
}
