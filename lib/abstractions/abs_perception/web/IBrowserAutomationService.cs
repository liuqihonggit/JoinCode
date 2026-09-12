namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 浏览器自动化服务接口 — 对齐TS版 WebBrowserTool 的 screenshot/evaluate 能力
/// 默认实现为 NoOp（不支持），安装 PuppeteerSharp 卫星包后可启用
/// </summary>
public interface IBrowserAutomationService
{
    /// <summary>
    /// 浏览器自动化是否可用（PuppeteerSharp 已安装且 Chromium 已下载）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 对指定URL截图
    /// </summary>
    /// <param name="url">目标URL</param>
    /// <param name="waitMs">等待页面加载的毫秒数（默认3000）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>截图结果（PNG字节数据）</returns>
    Task<OperationResult<byte[]?>> ScreenshotAsync(string url, int waitMs = 3000, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在指定URL的页面上下文中执行JavaScript
    /// </summary>
    /// <param name="url">目标URL（先导航到此页面）</param>
    /// <param name="script">JavaScript 表达式</param>
    /// <param name="waitMs">等待页面加载的毫秒数（默认3000）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>JavaScript 执行结果</returns>
    Task<OperationResult<string?>> EvaluateAsync(string url, string script, int waitMs = 3000, CancellationToken cancellationToken = default);
}

