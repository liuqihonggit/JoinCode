namespace IO.Services;

/// <summary>
/// Chrome 集成服务 — 检测 Chrome 扩展、管理连接状态与默认启用开关
/// </summary>
[Register(typeof(IChromeIntegrationService), ServiceLifetime.Singleton)]
public sealed partial class ChromeIntegrationService : ServiceEntity, IChromeIntegrationService, IDisposable
{
    private bool _isConnected;
    private bool _isDefaultEnabled;
    private bool _initialized;
    private readonly AsyncLock _initLock = new();
    private readonly IProcessService _processService;
    private readonly ILogger<ChromeIntegrationService>? _logger;
    private readonly IConfigurationService? _configService;
    private bool _disposed;

    /// <summary>
    /// 构造 Chrome 集成服务实例
    /// </summary>
    /// <param name="processService">进程服务抽象，用于查找 Chrome 可执行文件与打开 URL</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="configService">配置服务，用于持久化默认启用开关</param>
    public ChromeIntegrationService(
        IProcessService processService,
        ILogger<ChromeIntegrationService>? logger = null,
        IConfigurationService? configService = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
        _configService = configService;
    }

    /// <summary>
    /// 是否已安装 Chrome 扩展 — 通过查找 chrome 可执行文件判断
    /// </summary>
    public bool IsExtensionInstalled
    {
        get
        {
            try
            {
                return Task.Run(() => _processService.FindExecutableAsync("chrome")).GetAwaiter().GetResult() != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 是否已连接 Chrome 扩展
    /// </summary>
    public bool IsConnected => _isConnected;
    /// <summary>
    /// 是否默认启用 Chrome 集成 — 首次访问时从配置懒加载
    /// </summary>
    public bool IsDefaultEnabled
    {
        get
        {
            EnsureInitialized();
            return _isDefaultEnabled;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        using var guard = _initLock.TryLock();
        if (guard is null) return;
        if (_initialized) return;
        try { _isDefaultEnabled = Task.Run(() => ReadDefaultEnabledAsync()).GetAwaiter().GetResult(); }
        catch { _isDefaultEnabled = false; }
        _initialized = true;
    }

    /// <summary>
    /// 异步连接 Chrome 扩展 — 未检测到扩展时返回 false
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>连接成功返回 true；扩展未安装返回 false</returns>
    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (!IsExtensionInstalled)
        {
            _logger?.LogWarning("Chrome 扩展未检测到");
            return Task.FromResult(false);
        }

        _isConnected = true;
        _logger?.LogInformation("Chrome 扩展已连接");
        return Task.FromResult(true);
    }

    /// <summary>
    /// 异步断开 Chrome 扩展连接
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _isConnected = false;
        _logger?.LogInformation("Chrome 扩展已断开");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步打开 Chrome 扩展页面 — 非交互环境下跳过
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task OpenExtensionPageAsync(CancellationToken ct = default)
    {
        if (TestEnvironmentDetector.IsNonInteractive)
        {
            _logger?.LogInformation("非交互环境,跳过打开 Chrome 扩展页面");
            return;
        }

        try
        {
            await _processService.OpenAsync(JccEndpoints.ChromeIntegrationUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "打开 Chrome 扩展页面失败");
        }
    }

    /// <summary>
    /// 异步切换默认启用开关 — 翻转当前状态并持久化到配置
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>切换后的默认启用状态</returns>
    public async Task<bool> ToggleDefaultEnabledAsync(CancellationToken ct = default)
    {
        _isDefaultEnabled = !_isDefaultEnabled;

        if (_configService != null)
        {
            await _configService.SetAsync("chrome.defaultEnabled", _isDefaultEnabled ? "true" : "false", ct).ConfigureAwait(false);
        }

        _logger?.LogInformation("Chrome 默认启用: {Enabled}", _isDefaultEnabled);
        return _isDefaultEnabled;
    }

    private async Task<bool> ReadDefaultEnabledAsync()
    {
        if (_configService == null) return false;
        var value = await _configService.GetAsync("chrome.defaultEnabled").ConfigureAwait(false);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 释放资源 — 释放初始化锁
    /// </summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initLock.Dispose();
            base.Dispose();
    }
}
