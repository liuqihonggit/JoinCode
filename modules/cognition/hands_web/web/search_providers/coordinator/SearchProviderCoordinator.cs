namespace Services.Web;

/// <summary>
/// 多搜索引擎供应商调度器 — 按 <see cref="ISearchProvider.Priority"/> 升序遍历可用供应商，
/// 第一个 <see cref="SearchProviderResult.Success"/> 即返回；全失败时聚合错误消息。
/// 纵深防御：兜底供应商（<see cref="SubAgentSearchProvider"/>，Priority=999，IsAvailable 永真）保证最后防线。
/// </summary>
[Register(typeof(ISearchProviderCoordinator), ServiceLifetime.Singleton)]
public sealed partial class SearchProviderCoordinator : ServiceEntity, ISearchProviderCoordinator {
    private readonly ImmutableArray<ISearchProvider> _orderedProviders;
    private readonly ITelemetryService? _telemetryService;
    private readonly ILogger<SearchProviderCoordinator>? _logger;

    /// <summary>
    /// 初始化 <see cref="SearchProviderCoordinator"/> 实例。
    /// </summary>
    /// <param name="providers">搜索引擎供应商集合，由 DI 注入。按 <see cref="ISearchProvider.Priority"/> 升序排列。</param>
    /// <param name="telemetryService">可选遥测服务，记录调度指标。</param>
    /// <param name="logger">可选日志记录器。</param>
    public SearchProviderCoordinator(
        IEnumerable<ISearchProvider> providers,
        ITelemetryService? telemetryService = null,
        ILogger<SearchProviderCoordinator>? logger = null) {
        _orderedProviders = providers.OrderBy(p => p.Priority).ToImmutableArray();
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WebSearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) {
        var stopwatch = Stopwatch.StartNew();
        var failedProviders = new List<(string Name, string Error)>();
        var availableCount = 0;

        foreach (var provider in _orderedProviders) {
            if (!provider.IsAvailable) {
                _logger?.LogDebug("搜索供应商 {Name} 不可用，跳过", provider.Name);
                continue;
            }
            availableCount++;

            try {
                _logger?.LogDebug("尝试搜索供应商 {Name} (Priority={Priority})", provider.Name, provider.Priority);
                var result = await provider.SearchAsync(request, cancellationToken).ConfigureAwait(false);

                if (result.Success) {
                    stopwatch.Stop();
                    _logger?.LogInformation("搜索成功，供应商 {Name}，结果数 {Count}", provider.Name, result.Results.Count);
                    RecordSearchMetrics(true, provider.Name, result.Results.Count);
                    return new WebSearchResult(
                        true, request.Query, result.Results, result.Results.Count,
                        DurationSeconds: stopwatch.Elapsed.TotalSeconds);
                }

                failedProviders.Add((provider.Name, result.ErrorMessage ?? "Unknown error"));
                _logger?.LogWarning("搜索供应商 {Name} 失败: {Error}", provider.Name, result.ErrorMessage);
                RecordSearchMetrics(false, provider.Name, 0);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                failedProviders.Add((provider.Name, ex.Message));
                _logger?.LogError(ex, "搜索供应商 {Name} 抛出异常", provider.Name);
                RecordSearchMetrics(false, provider.Name, 0);
            }
        }

        stopwatch.Stop();
        var aggregateError = availableCount == 0
            ? "No search provider is available. Configure at least one provider (DEEPSEEK_API_KEY/TAVILY_API_KEY/SERPER_API_KEY/BRAVE_API_KEY/SEARXNG_URL) or ensure SubAgent fallback is registered."
            : "All search providers failed: " + string.Join("; ", failedProviders.Select(p => $"{p.Name}({p.Error})"));

        _logger?.LogError("所有搜索供应商失败。可用 {Available}，失败 {Failed}", availableCount, failedProviders.Count);
        return new WebSearchResult(
            false, request.Query, [], 0,
            DurationSeconds: stopwatch.Elapsed.TotalSeconds,
            ErrorMessage: aggregateError);
    }

    private void RecordSearchMetrics(bool isSuccess, string providerName, int resultCount)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "web.search.provider.count", providerName, isSuccess, "Web search provider invocation count");
}

/// <summary>
/// 搜索供应商调度器接口 — <see cref="WebService"/> 通过此接口委托多供应商搜索。
/// </summary>
public interface ISearchProviderCoordinator {
    /// <summary>
    /// 异步执行多供应商搜索，按优先级遍历可用供应商，返回第一个成功结果或聚合错误。
    /// </summary>
    /// <param name="request">统一搜索请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>搜索结果。</returns>
    Task<WebSearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
