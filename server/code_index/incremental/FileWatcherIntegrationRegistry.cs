namespace JoinCode.CodeIndex;

/// <summary>
/// 多仓库文件监听注册表 — Actor 邮箱模型，所有状态由 Consumer 线程独占访问，无锁无死锁。
/// <para>订阅 ICodeIndexerRegistry 的 RepoRegistered/RepoUnregistered 事件，按 repo_id 隔离管理 FileWatcherIntegration 实例。</para>
/// <para>事件处理器用 TrySend(Tell 模式) 投递命令到邮箱，不阻塞、不需要 await。</para>
/// <para>查询用 SendAsync + AskAwait(Ask 模式)，内置死锁检测+超时守卫。</para>
/// <para>Dispose: base.DisposeAsync() 等 Consumer 退出后，直接遍历 _watchers 释放(Consumer 已退出，无并发)。</para>
/// </summary>
[Register(typeof(FileWatcherIntegrationRegistry), ServiceLifetime.Singleton)]
public sealed class FileWatcherIntegrationRegistry : ActorBase<FileWatcherRegistryCommand, Unit> {
    private readonly ICodeIndexerRegistry _registry;
    private readonly IFileSystem _fs;
    private readonly ILogger<FileWatcherIntegrationRegistry>? _logger;
    private readonly Dictionary<string, FileWatcherIntegration> _watchers = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>
    /// 构造函数 — 注入索引注册表、文件系统与日志器，并订阅仓库注册/注销事件
    /// </summary>
    /// <param name="registry">代码索引注册表</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">日志器（可选）</param>
    public FileWatcherIntegrationRegistry(ICodeIndexerRegistry registry, IFileSystem fs, ILogger<FileWatcherIntegrationRegistry>? logger = null)
        : base(new ActorBackpressure(1024, BoundedChannelFullMode.DropOldest)) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(fs);
        _registry = registry;
        _fs = fs;
        _logger = logger;
        _registry.RepoRegistered += OnRepoRegistered;
        _registry.RepoUnregistered += OnRepoUnregistered;
    }

    private void OnRepoRegistered(object? sender, RepoRegisteredEventArgs e) {
        if (_disposed != 0) return;
        TrySend(new FileWatcherRegistryCommand.RegisterWatcher(e.RepoId, e.Indexer, e.WorkspaceRoot));
    }

    private void OnRepoUnregistered(object? sender, RepoUnregisteredEventArgs e) {
        if (_disposed != 0) return;
        TrySend(new FileWatcherRegistryCommand.UnregisterWatcher(e.RepoId));
    }

    /// <summary>
    /// 查询指定仓库的 watcher 是否正在运行 — Ask 模式，内置死锁检测+超时守卫
    /// </summary>
    public async Task<bool> IsWatchingAsync(string repoId) {
        if (_disposed != 0) return false;
        var tcs = new TaskCompletionSource<bool>();
        await SendAsync(new FileWatcherRegistryCommand.QueryIsWatching(repoId, tcs)).ConfigureAwait(false);
        return await AskAwait(tcs).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取所有正在监听的仓库 ID — Ask 模式，内置死锁检测+超时守卫
    /// </summary>
    public async Task<IReadOnlyList<string>> GetWatchingRepoIdsAsync() {
        if (_disposed != 0) return [];
        var tcs = new TaskCompletionSource<IReadOnlyList<string>>();
        await SendAsync(new FileWatcherRegistryCommand.QueryWatchingRepoIds(tcs)).ConfigureAwait(false);
        return await AskAwait(tcs).ConfigureAwait(false);
    }

    /// <summary>
    /// 命令分发 — 由 Consumer 线程串行调用，所有状态访问无需锁
    /// </summary>
    protected override ValueTask HandleAsync(FileWatcherRegistryCommand cmd, CancellationToken ct) {
        return cmd switch {
            FileWatcherRegistryCommand.RegisterWatcher c => HandleRegisterAsync(c),
            FileWatcherRegistryCommand.UnregisterWatcher c => HandleUnregisterAsync(c),
            FileWatcherRegistryCommand.QueryIsWatching c => HandleQueryIsWatching(c),
            FileWatcherRegistryCommand.QueryWatchingRepoIds c => HandleQueryWatchingRepoIds(c),
            _ => ValueTask.CompletedTask
        };
    }

    private async ValueTask HandleRegisterAsync(FileWatcherRegistryCommand.RegisterWatcher cmd) {
        if (_watchers.ContainsKey(cmd.RepoId)) {
            _logger?.LogWarning("repo {RepoId} 已有 watcher，跳过重复注册", cmd.RepoId);
            return;
        }
        var watcher = new FileWatcherIntegration(cmd.Indexer, cmd.WorkspaceRoot, _fs, onError: null);
        _watchers[cmd.RepoId] = watcher;
        try {
            await watcher.StartAsync(CancellationToken.None).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "启动 watcher 失败 repo={RepoId}", cmd.RepoId);
            _watchers.Remove(cmd.RepoId);
            await watcher.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask HandleUnregisterAsync(FileWatcherRegistryCommand.UnregisterWatcher cmd) {
        if (!_watchers.Remove(cmd.RepoId, out var watcher)) return;
        try {
            await watcher.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await watcher.DisposeAsync().ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "停止/释放 watcher 失败 repo={RepoId}", cmd.RepoId);
        }
    }

    private ValueTask HandleQueryIsWatching(FileWatcherRegistryCommand.QueryIsWatching cmd) {
        cmd.Reply.SetResult(_watchers.ContainsKey(cmd.RepoId));
        return ValueTask.CompletedTask;
    }

    private ValueTask HandleQueryWatchingRepoIds(FileWatcherRegistryCommand.QueryWatchingRepoIds cmd) {
        cmd.Reply.SetResult([.. _watchers.Keys]);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 释放资源 — 解除事件订阅，等待 Consumer 退出后释放所有 watcher
    /// </summary>
#pragma warning disable JCC9304 // Actor 模式: base.DisposeAsync() 等 Consumer 退出后才能安全访问 _watchers(Consumer 已退出，无并发)
    public override async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _registry.RepoRegistered -= OnRepoRegistered;
        _registry.RepoUnregistered -= OnRepoUnregistered;

        await base.DisposeAsync().ConfigureAwait(false);

        foreach (var kvp in _watchers) {
            try {
                await kvp.Value.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await kvp.Value.DisposeAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "释放 watcher 失败 repo={RepoId}", kvp.Key);
            }
        }
        _watchers.Clear();
    }
#pragma warning restore JCC9304
}

/// <summary>
/// FileWatcherIntegrationRegistry Actor 命令类型 — Tell 模式(注册/注销) + Ask 模式(查询)
/// </summary>
public abstract record FileWatcherRegistryCommand {
    /// <summary>注册 watcher — Tell 模式，由 RepoRegistered 事件触发</summary>
    public sealed record RegisterWatcher(string RepoId, ICodeIndexer Indexer, string WorkspaceRoot) : FileWatcherRegistryCommand;
    /// <summary>注销 watcher — Tell 模式，由 RepoUnregistered 事件触发</summary>
    public sealed record UnregisterWatcher(string RepoId) : FileWatcherRegistryCommand;
    /// <summary>查询 watcher 是否运行 — Ask 模式，通过 Reply 返回结果</summary>
    public sealed record QueryIsWatching(string RepoId, TaskCompletionSource<bool> Reply) : FileWatcherRegistryCommand;
    /// <summary>查询所有监听仓库 ID — Ask 模式，通过 Reply 返回结果</summary>
    public sealed record QueryWatchingRepoIds(TaskCompletionSource<IReadOnlyList<string>> Reply) : FileWatcherRegistryCommand;
}
