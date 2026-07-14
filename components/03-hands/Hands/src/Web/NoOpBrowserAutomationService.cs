namespace Services.Web;

/// <summary>
/// 浏览器自动化服务默认实现 — 未安装 PuppeteerSharp 卫星包时使用
/// 所有操作返回"不支持"，对齐TS版 WebBrowserTool 未启用时的行为
/// </summary>
[Register]
public sealed class NoOpBrowserAutomationService : IBrowserAutomationService
{
    public bool IsAvailable => false;

    public Task<OperationResult<byte[]?>> ScreenshotAsync(string url, int waitMs = 3000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<byte[]?>.Fail(L.T(StringKey.BrowserScreenshotNotSupported)));
    }

    public Task<OperationResult<string?>> EvaluateAsync(string url, string script, int waitMs = 3000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<string?>.Fail(L.T(StringKey.BrowserJsNotSupported)));
    }
}
