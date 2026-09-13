namespace Services.Web;

/// <summary>
/// 浏览器自动化服务默认实现 — 未安装 PuppeteerSharp 卫星包时使用
/// 所有操作返回"不支持"，对齐TS版 WebBrowserTool 未启用时的行为
/// </summary>
[Register(typeof(IBrowserAutomationService), ServiceLifetime.Singleton)]
public sealed partial class NoOpBrowserAutomationService : ServiceEntity, IBrowserAutomationService
{
    /// <summary>
    /// 获取浏览器自动化服务是否可用，始终返回 false。
    /// </summary>
    public bool IsAvailable => false;

    /// <summary>
    /// 异步对指定 URL 页面截图 — 当前实现返回"不支持"。
    /// </summary>
    /// <param name="url">目标页面 URL。</param>
    /// <param name="waitMs">页面加载等待时长（毫秒），默认 3000。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失败的操作结果，提示截图功能未启用。</returns>
    public Task<OperationResult<byte[]?>> ScreenshotAsync(string url, int waitMs = 3000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<byte[]?>.Fail(L.T(StringKey.BrowserScreenshotNotSupported)));
    }

    /// <summary>
    /// 异步在指定 URL 页面执行 JavaScript 脚本 — 当前实现返回"不支持"。
    /// </summary>
    /// <param name="url">目标页面 URL。</param>
    /// <param name="script">待执行的 JavaScript 脚本。</param>
    /// <param name="waitMs">页面加载等待时长（毫秒），默认 3000。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失败的操作结果，提示 JS 执行功能未启用。</returns>
    public Task<OperationResult<string?>> EvaluateAsync(string url, string script, int waitMs = 3000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<string?>.Fail(L.T(StringKey.BrowserJsNotSupported)));
    }
}
