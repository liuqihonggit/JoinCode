namespace Core.Plugins;

public interface IPluginHotReloader : IAsyncDisposable
{
    Task StartWatchingAsync(string pluginDirectory, CancellationToken ct = default);
    Task StopWatchingAsync(CancellationToken ct = default);
    bool IsWatching { get; }
    event EventHandler<PluginReloadEventArgs>? PluginReloading;
    event EventHandler<PluginReloadEventArgs>? PluginReloaded;
}

public sealed partial class PluginReloadEventArgs : EventArgs
{
    public required string PluginName { get; init; }
    public required string PluginPath { get; init; }
    public required ReloadReason Reason { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ReloadReason>))]
public enum ReloadReason { FileChanged, FileCreated, FileDeleted, Manual }

[Register(typeof(IPluginHotReloader), ServiceLifetime.Singleton)]
public sealed partial class PluginHotReloader : IPluginHotReloader
{
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<PluginHotReloader>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private IFileSystemWatcher? _watcher;
    private readonly AsyncLock _reloadLock;
    private volatile bool _isWatching;

    public PluginHotReloader(
        IPluginManager pluginManager,
        IFileSystem fs,
        ILogger<PluginHotReloader>? logger = null,
        ITelemetryService? telemetryService = null)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _telemetryService = telemetryService;
        _reloadLock = new AsyncLock(nameof(PluginHotReloader));
    }

    public bool IsWatching => _isWatching;

    public event EventHandler<PluginReloadEventArgs>? PluginReloading;
    public event EventHandler<PluginReloadEventArgs>? PluginReloaded;

    public Task StartWatchingAsync(string pluginDirectory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);

        if (_isWatching)
        {
            _logger?.LogWarning("[PluginHotReloader] 已在监控中，忽略重复启动请求");
            return Task.CompletedTask;
        }

        if (!_fs.DirectoryExists(pluginDirectory))
        {
            throw new DirectoryNotFoundException($"[INF030] 插件目录不存在: {pluginDirectory}");
        }

        _watcher = _fs.Watch(pluginDirectory);
        _watcher.IncludeSubdirectories = true;
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
        _watcher.Filter = "*.*";

        _watcher.DebouncedChanged += OnFileChanged;
        _watcher.DebouncedCreated += OnFileCreated;
        _watcher.DebouncedDeleted += OnFileDeleted;
        _watcher.EnableRaisingEvents = true;

        _isWatching = true;

        _logger?.LogInformation("[PluginHotReloader] 开始监控插件目录: {Directory}", pluginDirectory);

        return Task.CompletedTask;
    }

    public Task StopWatchingAsync(CancellationToken ct = default)
    {
        if (!_isWatching)
        {
            return Task.CompletedTask;
        }

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _isWatching = false;

        _logger?.LogInformation("[PluginHotReloader] 已停止监控");

        return Task.CompletedTask;
    }

    private void OnFileChanged(object? sender, FileChangedEventArgs e)
    {
        _ = ReloadPluginAsync(Path.GetFileNameWithoutExtension(e.FullPath), e.FullPath, ReloadReason.FileChanged);
    }

    private void OnFileCreated(object? sender, FileChangedEventArgs e)
    {
        _ = ReloadPluginAsync(Path.GetFileNameWithoutExtension(e.FullPath), e.FullPath, ReloadReason.FileCreated);
    }

    private void OnFileDeleted(object? sender, FileChangedEventArgs e)
    {
        _ = ReloadPluginAsync(Path.GetFileNameWithoutExtension(e.FullPath), e.FullPath, ReloadReason.FileDeleted);
    }

    internal async Task ReloadPluginAsync(string pluginName, string filePath, ReloadReason reason)
    {
        using var guard = await _reloadLock.TryLockAsync().ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_reloadLock.Name}' 等待超时");
        var args = new PluginReloadEventArgs
        {
            PluginName = pluginName,
            PluginPath = filePath,
            Reason = reason
        };

        NotifyReloading(args);

        _logger?.LogInformation("[PluginHotReloader] 重载插件: {Plugin}, 原因: {Reason}", pluginName, reason);

        if (_pluginManager.IsPluginLoaded(pluginName))
        {
            await _pluginManager.UnloadPluginAsync(pluginName, CancellationToken.None).ConfigureAwait(false);
        }

        if (reason != ReloadReason.FileDeleted && _fs.FileExists(filePath))
        {
            try
            {
                await _pluginManager.LoadExternalPluginAsync(filePath, pluginName, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[PluginHotReloader] 重载插件 '{Plugin}' 失败", pluginName);
            }
        }

        NotifyReloaded(args);

        _telemetryService?.RecordCount("plugin.hotreload.count", new Dictionary<string, string> { ["reason"] = reason.ToString(), ["success"] = true.ToString() }, "count", "Plugin hot reload count");
    }

    /// <summary>
    /// 触发 PluginReloading 事件 — 逐订阅者隔离异常，单个订阅者失败不中断重载链
    /// </summary>
    private void NotifyReloading(PluginReloadEventArgs args)
    {
        RaiseEventIsolated(PluginReloading, "PluginReloading", args);
    }

    /// <summary>
    /// 触发 PluginReloaded 事件 — 逐订阅者隔离异常，单个订阅者失败不影响其他订阅者
    /// </summary>
    private void NotifyReloaded(PluginReloadEventArgs args)
    {
        RaiseEventIsolated(PluginReloaded, "PluginReloaded", args);
    }

    /// <summary>
    /// 快照订阅者并逐个调用，隔离每个订阅者的异常（对齐 ThreadSafeListenerList.Notify 约定）
    /// </summary>
    private void RaiseEventIsolated(EventHandler<PluginReloadEventArgs>? handler, string eventName, PluginReloadEventArgs args)
    {
        if (handler is null) return;

        foreach (EventHandler<PluginReloadEventArgs> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, args);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[PluginHotReloader] {EventName} 订阅者抛异常，已隔离: {Plugin}", eventName, args.PluginName);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopWatchingAsync().ConfigureAwait(false);
        _reloadLock.Dispose();
    }

}
