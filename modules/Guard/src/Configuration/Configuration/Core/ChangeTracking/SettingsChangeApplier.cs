namespace Core.Configuration;

/// <summary>
/// 设置变更应用器 — 对齐 TS 版 applySettingsChange
/// 当 settings.json 变更时，通过中间件管道重新加载设置并更新相关服务状态
/// </summary>
[Register]
public sealed partial class SettingsChangeApplier : ISettingsChangeApplier, IDisposable
{
    private readonly IConfigChangeNotifier _configChangeNotifier;
    private readonly MiddlewarePipeline<SettingsContext> _pipeline;
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<SettingsChangeApplier>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _isDisposed;

    public SettingsChangeApplier(
        IConfigChangeNotifier configChangeNotifier,
        MiddlewarePipeline<SettingsContext> pipeline,
        IFileSystem fs,
        ILogger<SettingsChangeApplier>? logger = null,
        ITelemetryService? telemetryService = null)
    {
        _configChangeNotifier = configChangeNotifier;
        _pipeline = pipeline;
        _fs = fs;
        _logger = logger;
        _telemetryService = telemetryService;

        _configChangeNotifier.ConfigChanged += OnConfigChanged;
    }

    /// <summary>
    /// 手动触发设置重新加载 — 对齐 TS 版 applySettingsChange(source, setAppState)
    /// </summary>
    public async Task ApplySettingsChangeAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("正在应用设置变更...");

        try
        {
            var context = new SettingsContext
            {
                FileSystem = _fs,
                Logger = _logger,
                TelemetryService = _telemetryService,
            };

            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            _telemetryService?.RecordCount("guard.settings.apply.count", [], "count", "Settings apply count");
            _logger?.LogInformation("设置变更已应用");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "应用设置变更失败");
            _telemetryService?.RecordCount("guard.settings.apply.error.count", [], "count", "Settings apply error count");
        }
    }

    private void OnConfigChanged(object? sender, ConfigChangeEventArgs e)
    {
        // 只处理 settings.json 变更
        var fileName = Path.GetFileName(e.FilePath);
        if (!string.Equals(fileName, AppDataConstants.SettingsFileName, StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger?.LogInformation("检测到配置文件变更: {Path} ({ChangeType})", e.FilePath, e.ChangeType);

        // 异步应用变更，不阻塞文件监听线程
        _ = ApplySettingsChangeAsync(_disposeCts.Token).WaitAsync(TimeSpan.FromSeconds(10), _disposeCts.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed)) return;

        _configChangeNotifier.ConfigChanged -= OnConfigChanged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
