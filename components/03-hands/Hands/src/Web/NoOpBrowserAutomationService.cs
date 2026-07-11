namespace Services.Web;

/// <summary>
/// 浏览器自动化服务默认实现 — 未安装 PuppeteerSharp 卫星包时使用
/// 所有操作返回"不支持"，对齐TS版 WebBrowserTool 未启用时的行为
/// </summary>
[Register]
public sealed class NoOpBrowserAutomationService : IBrowserAutomationService
{
    public bool IsAvailable => false;

    public Task<BrowserScreenshotResult> ScreenshotAsync(string url, int waitMs = 3000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BrowserScreenshotResult(
            Success: false,
            ErrorMessage: L.T(StringKey.BrowserScreenshotNotSupported)));
    }

    public Task<BrowserEvaluateResult> EvaluateAsync(string url, string script, int waitMs = 3000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BrowserEvaluateResult(
            Success: false,
            ErrorMessage: L.T(StringKey.BrowserJsNotSupported)));
    }
}
