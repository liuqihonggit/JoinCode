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

[Register(typeof(IPluginHotReloader))]
public sealed partial class PluginHotReloader : IPluginHotReloader
{
    private readonly IPluginManager _pluginManager;
    [Inject] private readonly ILogger<PluginHotReloader>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private IFileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _reloadLock;
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
        _reloadLock = new SemaphoreSlim(1, 1);
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
            throw new DirectoryNotFoundException($"插件目录不存在: {pluginDirectory}");
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

    private async Task ReloadPluginAsync(string pluginName, string filePath, ReloadReason reason)
    {
        await _reloadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var args = new PluginReloadEventArgs
            {
                PluginName = pluginName,
                PluginPath = filePath,
                Reason = reason
            };

            PluginReloading?.Invoke(this, args);

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

            PluginReloaded?.Invoke(this, args);

            _telemetryService?.RecordCount("plugin.hotreload.count", new Dictionary<string, string> { ["reason"] = reason.ToString(), ["success"] = true.ToString() }, "count", "Plugin hot reload count");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopWatchingAsync().ConfigureAwait(false);
        _reloadLock.Dispose();
    }

}
