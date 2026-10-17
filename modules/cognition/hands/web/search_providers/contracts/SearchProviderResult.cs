namespace Services.Web;

/// <summary>
/// 统一搜索结果 — 所有搜索引擎供应商的输出契约。
/// <see cref="Success"/> 为 false 时，<see cref="SearchProviderCoordinator"/> 将尝试下一个供应商。
/// </summary>
/// <param name="Success">是否搜索成功。false 表示此供应商失败（API 错误、无结果、异常等），非业务意义上的"零结果"。</param>
/// <param name="ProviderName">供应商名称，用于日志追溯。</param>
/// <param name="Results">搜索结果列表。成功但零结果时为空列表。</param>
/// <param name="DurationSeconds">耗时（秒），用于指标。</param>
/// <param name="ErrorMessage">失败时的错误消息，聚合到最终 <see cref="WebSearchResult.ErrorMessage"/>。</param>
public sealed record SearchProviderResult(
    bool Success,
    string ProviderName,
    List<SearchResultItem> Results,
    double DurationSeconds = 0,
    string? ErrorMessage = null);
