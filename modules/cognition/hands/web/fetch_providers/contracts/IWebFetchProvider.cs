namespace Services.Web;

/// <summary>
/// 网页获取供应商统一抽象 — 所有网页获取供应商（DeepSeek Fetch 等）实现此接口。
/// 由 <see cref="WebService.FetchAsync"/> 遍历可用供应商，第一个成功即返回；全失败回退到中间件管道。
/// </summary>
public interface IWebFetchProvider {
    /// <summary>
    /// 获取供应商名称（如 "DeepSeek"），用于日志和错误聚合。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取供应商是否可用。未配置 API key 或不满足前置条件时返回 false，跳过此供应商。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 异步获取指定 URL 的网页内容。实现方应吞掉自身异常并返回失败的 <see cref="WebFetchResult"/>，
    /// 仅在不可恢复的框架异常时抛出。
    /// </summary>
    /// <param name="url">待获取的网页 URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>获取结果，<see cref="WebFetchResult.Success"/> 为 false 时表示此供应商失败。</returns>
    Task<WebFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default);
}
