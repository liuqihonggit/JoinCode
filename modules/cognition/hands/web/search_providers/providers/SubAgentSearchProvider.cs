namespace Services.Web;

/// <summary>
/// 子代理兜底搜索供应商 — 最后防线，永远 <see cref="IsAvailable"/>=true。
/// 用 <see cref="IApiClient"/> 抓取 DuckDuckGo HTML 搜索页，再用 <see cref="IQueryService"/> 让 LLM 从 HTML 提取结构化结果。
/// LLM 不可用时回退到正则提取链接。无 API key 依赖，仅依赖网络 + 可选 LLM。
/// 优先级 999（最低，仅当所有其他供应商都失败时触发）。
/// </summary>
[Register(typeof(ISearchProvider), ServiceLifetime.Singleton)]
public sealed partial class SubAgentSearchProvider : ServiceEntity, ISearchProvider {
    private const string DuckDuckGoEndpoint = "https://html.duckduckgo.com/html/";
    private const int DefaultPriority = 999;
    private const int MaxHtmlLength = 50000;

    private readonly IApiClient _apiClient;
    private readonly IQueryService? _queryService;
    private readonly ProviderConfig? _providerConfig;
    private readonly ILogger<SubAgentSearchProvider>? _logger;

    /// <summary>
    /// 初始化 <see cref="SubAgentSearchProvider"/> 实例。
    /// </summary>
    /// <param name="apiClient">API 客户端，用于抓取 DuckDuckGo 搜索页。</param>
    /// <param name="queryService">可选查询服务，用于 LLM 提取结构化结果。为 null 时回退到正则。</param>
    /// <param name="providerConfig">可选供应商配置，用于获取快速模型 ID。</param>
    /// <param name="logger">可选日志记录器。</param>
    public SubAgentSearchProvider(
        IApiClient apiClient,
        IQueryService? queryService = null,
        ProviderConfig? providerConfig = null,
        ILogger<SubAgentSearchProvider>? logger = null) {
        _apiClient = apiClient;
        _queryService = queryService;
        _providerConfig = providerConfig;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SubAgent";

    /// <inheritdoc />
    public int Priority => DefaultPriority;

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task<SearchProviderResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) {
        var stopwatch = Stopwatch.StartNew();
        try {
            var html = await FetchDuckDuckGoHtmlAsync(request.Query, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(html)) {
                stopwatch.Stop();
                return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, "DuckDuckGo returned empty content");
            }

            var truncatedHtml = html.Length > MaxHtmlLength ? html[..MaxHtmlLength] : html;

            var results = await ExtractResultsWithLlmAsync(truncatedHtml, request.MaxResults, cancellationToken).ConfigureAwait(false);
            if (results.Count == 0) {
                results = ExtractResultsWithRegex(truncatedHtml, request.MaxResults);
            }

            stopwatch.Stop();
            return new SearchProviderResult(true, Name, results, stopwatch.Elapsed.TotalSeconds);
        } catch (Exception ex) {
            stopwatch.Stop();
            _logger?.LogError(ex, "SubAgent 兜底搜索失败: {Query}", request.Query);
            return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, ex.Message);
        }
    }

    private async Task<string> FetchDuckDuckGoHtmlAsync(string query, CancellationToken cancellationToken) {
        var queryParams = new Dictionary<string, string> { ["q"] = query };
        var apiRequest = new ApiRequest {
            Method = HttpMethod.Get,
            Path = DuckDuckGoEndpoint,
            QueryParams = queryParams,
            SkipRetry = true
        };
        var response = await _apiClient.SendAsync(apiRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<SearchResultItem>> ExtractResultsWithLlmAsync(string html, int maxResults, CancellationToken cancellationToken) {
        var queryService = _queryService;
        if (queryService is null) return [];

        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage($"""
            You are a search result extractor. Extract the top {maxResults} search results from the given HTML.
            Return ONLY a JSON array where each element has "title", "url", and "snippet" string fields.
            If no results found, return an empty array.
            """);
        chatHistory.AddUserMessage(html);

        var options = new ChatOptions {
            Temperature = 0,
            MaxTokens = 4096,
            FastMode = true,
            FastModelId = _providerConfig?.Definition?.DefaultFastModelId
        };

        try {
            var messages = await queryService.GetApiMessageContentsAsync(
                chatHistory, options, cancellationToken: cancellationToken).ConfigureAwait(false);

            var json = string.Concat(messages.Select(m => m.Content ?? ""));
            return ParseLlmJsonResults(json);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "LLM 提取搜索结果失败，回退到正则");
            return [];
        }
    }

    private List<SearchResultItem> ParseLlmJsonResults(string json) {
        var results = new List<SearchResultItem>();
        var trimmed = json.AsSpan().Trim();
        if (trimmed.Length == 0 || trimmed[0] != '[') return results;

        try {
            using var doc = JsonDocument.Parse(trimmed.ToString());
            foreach (var item in doc.RootElement.EnumerateArray()) {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : null;

                var safeTitle = title ?? "";
                var safeUrl = url ?? "";
                if (string.IsNullOrEmpty(safeTitle) || string.IsNullOrEmpty(safeUrl)) continue;
                if (results.Any(r => r.Url.Equals(safeUrl, StringComparison.OrdinalIgnoreCase))) continue;

                results.Add(new SearchResultItem(safeTitle, safeUrl, Snippet: snippet));
            }
        } catch (JsonException ex) {
            _logger?.LogWarning(ex, "LLM 返回的 JSON 解析失败，由正则兜底");
        }
        return results;
    }

    private static List<SearchResultItem> ExtractResultsWithRegex(string html, int maxResults) {
        var results = new List<SearchResultItem>();
        var linkRegex = new Regex(@"<a[^>]*href=[""'](https?://[^""']+)[""'][^>]*>([^<]+)</a>", RegexOptions.Compiled);
        var matches = linkRegex.Matches(html);

        foreach (Match match in matches) {
            var url = match.Groups[1].Value.Trim();
            var title = match.Groups[2].Value.Trim();

            if (url.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(title) || title.Length < 3) continue;
            if (results.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase))) continue;

            results.Add(new SearchResultItem(title, url));
            if (results.Count >= maxResults) break;
        }
        return results;
    }
}
