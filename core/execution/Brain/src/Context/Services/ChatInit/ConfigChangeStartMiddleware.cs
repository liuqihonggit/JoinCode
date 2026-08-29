namespace Core.Context;

/// <summary>
/// 配置变更监控中间件 — 启动配置文件变更监控并处理变更事件
/// 同时实现 IAsyncDisposable，由 DI 容器在应用关闭时自动释放资源
/// </summary>
[Register(typeof(IChatInitMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ConfigChangeStartMiddleware : IChatInitMiddleware, IAsyncDisposable
{

    public ConfigChangeStartMiddleware(IFileSystem fs, IConfigChangeNotifier? configChangeNotifier = null, ISettingsChangeApplier? settingsChangeApplier = null, ILogger<ConfigChangeStartMiddleware>? logger = null)
    {
        _fs = fs;
        _configChangeNotifier = configChangeNotifier;
        _settingsChangeApplier = settingsChangeApplier;
        _logger = logger;
    }
    private readonly IConfigChangeNotifier? _configChangeNotifier;
    private readonly IFileSystem _fs;
    private readonly ISettingsChangeApplier? _settingsChangeApplier;
    private readonly ILogger<ConfigChangeStartMiddleware>? _logger;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    /// <summary>配置监控在成本恢复之后</summary>

    /// <summary>配置监控失败不应中断管道</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 启动配置文件变更监控
    /// </summary>
    public async Task InvokeAsync(ChatInitContext context, MiddlewareDelegate<ChatInitContext> next, CancellationToken ct)
    {
        if (_configChangeNotifier is not null)
        {
            var workingDir = _fs.GetCurrentDirectory();
            _configChangeNotifier.ConfigChanged += OnConfigChanged;
            _configChangeNotifier.StartMonitoring(workingDir);
            _logger?.LogInformation("[ConfigChange] 已启动配置文件变更监控，工作目录: {Dir}", workingDir);
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private void OnConfigChanged(object? sender, ConfigChangeEventArgs e)
    {
        if (_disposed != 0) return;

        // 对齐 TS 版 applySettingsChange — 自动更新 EffortLevel 和 Hook 配置
        // 注：不再向 LLM 注入 <system-reminder> 提示，仅应用设置热重载
        if (_settingsChangeApplier is not null && _disposed == 0)
        {
            _ = _settingsChangeApplier.ApplySettingsChangeAsync(_disposeCts.Token)
                .ConfigureAwait(false);
        }

        _logger?.LogInformation("[ConfigChange] 配置文件变更已应用热重载: {Path} ({ChangeType})", e.FilePath, e.ChangeType);
    }

    /// <summary>
    /// 释放资源：取消配置变更订阅、取消即发即忘操作
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        if (_configChangeNotifier is not null)
        {
            _configChangeNotifier.ConfigChanged -= OnConfigChanged;
            _configChangeNotifier.StopMonitoring();
        }
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
