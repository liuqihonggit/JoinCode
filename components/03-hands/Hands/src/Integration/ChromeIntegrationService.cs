using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class ChromeIntegrationService : IChromeIntegrationService, IDisposable
{
    private bool _isConnected;
    private bool _isDefaultEnabled;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly IProcessService _processService;
    [Inject] private readonly ILogger<ChromeIntegrationService>? _logger;
    private readonly IConfigurationService? _configService;

    public ChromeIntegrationService(
        IProcessService processService,
        ILogger<ChromeIntegrationService>? logger = null,
        IConfigurationService? configService = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
        _configService = configService;
    }

    public bool IsExtensionInstalled
    {
        get
        {
            try
            {
                return _processService.FindExecutableAsync("chrome").GetAwaiter().GetResult() != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsConnected => _isConnected;
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
        if (!_initLock.Wait(0)) return;
        try
        {
            if (_initialized) return;
            try { _isDefaultEnabled = ReadDefaultEnabled().GetAwaiter().GetResult(); }
            catch { _isDefaultEnabled = false; }
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

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

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _isConnected = false;
        _logger?.LogInformation("Chrome 扩展已断开");
        return Task.CompletedTask;
    }

    public async Task OpenExtensionPageAsync(CancellationToken ct = default)
    {
        if (TestEnvironmentDetector.IsNonInteractive)
        {
            _logger?.LogInformation("非交互环境,跳过打开 Chrome 扩展页面");
            return;
        }

        try
        {
            await _processService.OpenAsync("https://jcc.dev/chrome", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "打开 Chrome 扩展页面失败");
        }
    }

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

    private async Task<bool> ReadDefaultEnabled()
    {
        if (_configService == null) return false;
        var value = await _configService.GetAsync("chrome.defaultEnabled").ConfigureAwait(false);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _initLock.Dispose();
    }
}
