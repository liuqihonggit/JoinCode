namespace Services.Web;

/// <summary>
/// 搜索引擎供应商统一抽象 — 所有搜索引擎供应商（Tavily/Serper/Brave/SearXNG/Anthropic/SubAgent）实现此接口。
/// 由 <see cref="SearchProviderCoordinator"/> 按 <see cref="Priority"/> 升序调度。
/// </summary>
public interface ISearchProvider {
    /// <summary>
    /// 获取供应商名称（如 "Tavily"、"Serper"、"SubAgent"），用于日志和错误聚合。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取调度优先级，数值越小越优先。<see cref="SearchProviderCoordinator"/> 按升序遍历。
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 获取供应商是否可用。未配置 API key 或不满足前置条件时返回 false，调度器跳过此供应商。
    /// 兜底供应商（<see cref="SubAgentSearchProvider"/>）永远返回 true。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 异步执行搜索，返回结构化结果。实现方应吞掉自身异常并写入 <see cref="SearchProviderResult.ErrorMessage"/>，
    /// 仅在不可恢复的框架异常时抛出（由调度器捕获）。
    /// </summary>
    /// <param name="request">统一搜索请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>搜索结果，<see cref="SearchProviderResult.Success"/> 为 false 时表示此供应商失败，调度器将尝试下一个。</returns>
    Task<SearchProviderResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
