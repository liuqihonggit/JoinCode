namespace Core.DependencyInjection;

/// <summary>
/// 插件文件监控托管服务 — 在应用启动时启动 PluginFileWatcher，在停止时停止监控
/// </summary>
[Register(typeof(IHostedService), ServiceLifetime.Singleton)]
public sealed class PluginFileWatcherHostedService : IHostedService {
    private readonly PluginFileWatcher _watcher;
    private readonly ILogger<PluginFileWatcherHostedService>? _logger;

    /// <summary>构造</summary>
    public PluginFileWatcherHostedService(
        PluginFileWatcher watcher,
        ILogger<PluginFileWatcherHostedService>? logger = null) {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _logger = logger;
    }

    /// <summary>启动 — 调用 PluginFileWatcher.StartWatching()</summary>
    public Task StartAsync(CancellationToken cancellationToken) {
        try {
            _watcher.StartWatching();
            _logger?.LogInformation("[PluginFileWatcherHostedService] 插件文件监控已启动");
        } catch (Exception ex) {
            _logger?.LogError(ex, "[PluginFileWatcherHostedService] 启动插件文件监控失败");
        }
        return Task.CompletedTask;
    }

    /// <summary>停止 — 调用 PluginFileWatcher.StopWatchingAsync()</summary>
    public async Task StopAsync(CancellationToken cancellationToken) {
        try {
            await _watcher.StopWatchingAsync().ConfigureAwait(false);
            _logger?.LogInformation("[PluginFileWatcherHostedService] 插件文件监控已停止");
        } catch (Exception ex) {
            _logger?.LogError(ex, "[PluginFileWatcherHostedService] 停止插件文件监控失败");
        }
    }
}
