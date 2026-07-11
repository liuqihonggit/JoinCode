namespace Plugins.Browser;

/// <summary>
/// 基于 PuppeteerSharp 的浏览器自动化服务 — 卫星包实现
/// 对齐TS版 WebBrowserTool 的 screenshot/evaluate 能力
/// 仅在安装此包后通过 DI 注册替代 NoOpBrowserAutomationService
/// </summary>
[Register]
public sealed partial class PuppeteerBrowserAutomationService : IBrowserAutomationService, IAsyncDisposable
{
    [Inject] private readonly ILogger<PuppeteerBrowserAutomationService> _logger;
    private IBrowser? _browser;
    private bool _initialized;
    private bool _initializing;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public PuppeteerBrowserAutomationService(ILogger<PuppeteerBrowserAutomationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 浏览器是否可用 — 基于已初始化标志快速返回，不触发同步等待
    /// 首次调用 ScreenshotAsync/EvaluateAsync 时会懒初始化
    /// </summary>
    public bool IsAvailable => _initialized && _browser is not null;

    public async Task<BrowserScreenshotResult> ScreenshotAsync(string url, int waitMs = 3000, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        if (_browser is null)
        {
            return new BrowserScreenshotResult(Success: false, ErrorMessage: "Browser not available");
        }

        await using var page = await _browser.NewPageAsync().ConfigureAwait(false);
        try
        {
            var response = await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.Networkidle0],
                Timeout = waitMs
            }).ConfigureAwait(false);

            if (response is null || !response.Ok)
            {
                var status = response?.Status ?? 0;
                return new BrowserScreenshotResult(Success: false, ErrorMessage: $"Navigation failed with status {status}");
            }

            var screenshotData = await page.ScreenshotDataAsync(new ScreenshotOptions
            {
                Type = ScreenshotType.Png,
                FullPage = false
            }).ConfigureAwait(false);

            return new BrowserScreenshotResult(Success: true, PngData: screenshotData);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot failed for {Url}", url);
            return new BrowserScreenshotResult(Success: false, ErrorMessage: ex.Message);
        }
    }

    public async Task<BrowserEvaluateResult> EvaluateAsync(string url, string script, int waitMs = 3000, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        if (_browser is null)
        {
            return new BrowserEvaluateResult(Success: false, ErrorMessage: "Browser not available");
        }

        await using var page = await _browser.NewPageAsync().ConfigureAwait(false);
        try
        {
            if (!string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                var response = await page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.Networkidle0],
                    Timeout = waitMs
                }).ConfigureAwait(false);

                if (response is null || !response.Ok)
                {
                    var status = response?.Status ?? 0;
                    return new BrowserEvaluateResult(Success: false, ErrorMessage: $"Navigation failed with status {status}");
                }
            }

            // 使用泛型版本 EvaluateExpressionAsync<string> 获取返回值
            var result = await page.EvaluateExpressionAsync<string>(script).ConfigureAwait(false);
            var resultStr = result ?? "undefined";

            return new BrowserEvaluateResult(Success: true, Result: resultStr);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JavaScript evaluation failed on {Url}", url);
            return new BrowserEvaluateResult(Success: false, ErrorMessage: ex.Message);
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized || _initializing) return;
            _initializing = true;

            try
            {
                var browserFetcher = new BrowserFetcher();
                var installed = await browserFetcher.DownloadAsync().ConfigureAwait(false);
                if (installed is null)
                {
                    _logger.LogWarning("Failed to download Chromium browser");
                    return;
                }

                _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
                }).ConfigureAwait(false);

                _logger.LogInformation("PuppeteerSharp browser initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize PuppeteerSharp browser");
                _browser = null;
            }
            finally
            {
                _initialized = true;
                _initializing = false;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            try
            {
                await _browser.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose browser");
            }
            _browser = null;
        }

        _initLock.Dispose();
    }
}
