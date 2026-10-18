namespace Services.Web;

/// <summary>
/// Web服务实现 — URL获取、缓存、域名安全检查、重定向安全、HTML转Markdown
/// 对齐TS版 WebFetchTool/utils.ts 的核心逻辑
/// FetchAsync 优先尝试 IWebFetchProvider 供应商（如 DeepSeek Fetch），全失败回退到中间件管道
/// SearchAsync 直接实现
/// </summary>
[Register(typeof(IWebService), ServiceLifetime.Singleton)]
public sealed partial class WebService : ServiceEntity, IWebService {
    private readonly MiddlewarePipeline<WebContext> _pipeline;
    private readonly IWebFetchCache _cache;
    private readonly ITelemetryService? _telemetryService;
    private readonly ISearchProviderCoordinator _searchCoordinator;
    private readonly ImmutableArray<IWebFetchProvider> _fetchProviders;
    private readonly ILogger<WebService>? _logger;

    /// <summary>
    /// 初始化 <see cref="WebService"/> 实例。
    /// </summary>
    /// <param name="pipeline">Web 中间件管道，用于执行抓取流程（fetch provider 全失败时回退）。</param>
    /// <param name="cache">Web 抓取缓存。</param>
    /// <param name="searchCoordinator">搜索引擎供应商调度器，委托多供应商搜索（ADR 0138）。</param>
    /// <param name="fetchProviders">网页获取供应商集合（如 DeepSeek Fetch），按注入顺序遍历，第一个成功即返回。</param>
    /// <param name="telemetryService">可选的遥测服务，用于记录 Web 操作指标。</param>
    /// <param name="logger">可选日志记录器。</param>
    public WebService(
        MiddlewarePipeline<WebContext> pipeline,
        IWebFetchCache cache,
        ISearchProviderCoordinator searchCoordinator,
        IEnumerable<IWebFetchProvider> fetchProviders,
        ITelemetryService? telemetryService = null,
        ILogger<WebService>? logger = null) {
        _pipeline = pipeline;
        _cache = cache;
        _searchCoordinator = searchCoordinator;
        _fetchProviders = fetchProviders.ToImmutableArray();
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <summary>
    /// 异步执行 Web 搜索，委托 <see cref="ISearchProviderCoordinator"/> 按优先级遍历多个搜索引擎供应商。
    /// 纵深防御：所有供应商失败时由 <see cref="SubAgentSearchProvider"/> 兜底（ADR 0138）。
    /// </summary>
    /// <param name="query">搜索查询字符串。</param>
    /// <param name="allowedDomains">可选的允许域名白名单。</param>
    /// <param name="blockedDomains">可选的屏蔽域名黑名单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含搜索结果列表与耗时信息的搜索结果。</returns>
    public async Task<WebSearchResult> SearchAsync(string query, string[]? allowedDomains = null, string[]? blockedDomains = null, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(query)) {
            return new WebSearchResult(false, query, [], 0, ErrorMessage: "Search query cannot be empty");
        }

        if (allowedDomains is { Length: > 0 } && blockedDomains is { Length: > 0 }) {
            return new WebSearchResult(false, query, [], 0, ErrorMessage: "Cannot specify both allowed_domains and blocked_domains");
        }

        var request = new SearchRequest(query, allowedDomains, blockedDomains);
        var result = await _searchCoordinator.SearchAsync(request, cancellationToken).ConfigureAwait(false);

        RecordWebMetrics("search", result.Success, result.Results.Count);
        if (!result.Success) {
            _logger?.LogWarning("Web 搜索失败: {Query}, Error: {Error}", query, result.ErrorMessage);
        }
        return result;
    }

    /// <summary>
    /// 异步抓取指定 URL 内容。优先尝试 <see cref="IWebFetchProvider"/> 供应商（如 DeepSeek Fetch），
    /// 第一个成功即返回；全失败或无可用供应商时回退到中间件管道（HTTP 抓取 + HTML 转 Markdown）。
    /// </summary>
    /// <param name="url">待抓取的 URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含内容、状态、字节数等信息的抓取结果。</returns>
    public async Task<WebFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default) {
        foreach (var provider in _fetchProviders) {
            if (!provider.IsAvailable) continue;
            try {
                _logger?.LogDebug("尝试 fetch 供应商 {Name}", provider.Name);
                var result = await provider.FetchAsync(url, cancellationToken).ConfigureAwait(false);
                if (result.Success) {
                    RecordWebMetrics("fetch", true, result.Bytes);
                    return result;
                }
                _logger?.LogWarning("Fetch 供应商 {Name} 失败: {Error}", provider.Name, result.ErrorMessage);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                _logger?.LogError(ex, "Fetch 供应商 {Name} 抛出异常", provider.Name);
            }
        }

        var context = new WebContext {
            Url = url,
            CancellationToken = cancellationToken
        };

        try {
            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            return new WebFetchResult(false, url, ErrorMessage: ex.Message);
        }

        return context.Result ?? new WebFetchResult(false, url, ErrorMessage: "Pipeline completed without result");
    }

    /// <summary>
    /// 清理WebFetch缓存（会话清理时调用）
    /// </summary>
    public void ClearCache() => _cache.Clear();

    private void RecordWebMetrics(string operation, bool isSuccess, int size = 0)
        => ToolTelemetryHelper.RecordToolCount(_telemetryService, "web.operation.count", operation, isSuccess, "Web operation count");
}