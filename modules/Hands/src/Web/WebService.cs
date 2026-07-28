namespace Services.Web;

/// <summary>
/// Web服务实现 — URL获取、缓存、域名安全检查、重定向安全、HTML转Markdown
/// 对齐TS版 WebFetchTool/utils.ts 的核心逻辑
/// FetchAsync 通过中间件管道执行，SearchAsync 直接实现
/// </summary>
[Register]
public sealed partial class WebService : IWebService
{
    private readonly MiddlewarePipeline<WebContext> _pipeline;
    private readonly IWebFetchCache _cache;
    private readonly ITelemetryService? _telemetryService;
    private readonly IQueryService? _queryService;
    private readonly ProviderConfig? _providerConfig;
    [Inject] private readonly ILogger<WebService>? _logger;
    [Inject] private readonly IClockService _clock;

    public WebService(
        MiddlewarePipeline<WebContext> pipeline,
        IWebFetchCache cache,
        ITelemetryService? telemetryService = null,
        ILogger<WebService>? logger = null,
        IQueryService? queryService = null,
        ProviderConfig? providerConfig = null,
        IClockService? clock = null)
    {
        _pipeline = pipeline;
        _cache = cache;
        _telemetryService = telemetryService;
        _logger = logger;
        _queryService = queryService;
        _providerConfig = providerConfig;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<WebSearchResult> SearchAsync(string query, string[]? allowedDomains = null, string[]? blockedDomains = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new WebSearchResult(false, query, [], 0, ErrorMessage: "Search query cannot be empty");
        }

        if (allowedDomains is { Length: > 0 } && blockedDomains is { Length: > 0 })
        {
            return new WebSearchResult(false, query, [], 0, ErrorMessage: "Cannot specify both allowed_domains and blocked_domains");
        }

        // 检查 Provider 是否支持 WebSearch
        if (_providerConfig?.Definition is not { SupportsWebSearch: true })
        {
            return new WebSearchResult(false, query, [], 0,
                ErrorMessage: "Web search is not available for the current provider. It requires an Anthropic API connection with web_search support.");
        }

        if (_queryService == null)
        {
            return new WebSearchResult(false, query, [], 0, ErrorMessage: "Query service is not available");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 构造搜索请求 — 对齐 TS 版 WebSearchTool.call()
            var chatHistory = new MessageList();

            // 对齐 TS prompt.ts：注入搜索专用系统提示词
            chatHistory.AddSystemMessage(BuildWebSearchSystemPrompt());

            chatHistory.AddUserMessage($"Perform a web search for the query: {query}");

            // 通过 ExtensionData 传递 web_search 工具 schema
            var webSearchToolSchema = BuildWebSearchToolSchema(allowedDomains, blockedDomains);
            var options = new ChatOptions
            {
                Temperature = 0,
                MaxTokens = 4096,
                FastMode = true,
                FastModelId = _providerConfig.Definition.DefaultFastModelId,
                ExtensionData = new Dictionary<string, JsonElement>
                {
                    ["web_search_tool"] = webSearchToolSchema
                }
            };

            var results = await _queryService.GetApiMessageContentsAsync(
                chatHistory, options, cancellationToken: cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            // 优先从 metadata 提取结构化搜索结果（对齐 TS makeOutputFromSearchResponse）
            var searchResults = ExtractSearchResultsFromMetadata(results);

            // 回退：从文本内容中提取（兼容非 Anthropic provider）
            if (searchResults.Count == 0)
            {
                foreach (var msg in results)
                {
                    if (string.IsNullOrEmpty(msg.Content)) continue;
                    ExtractSearchResults(msg.Content, searchResults);
                }
            }

            RecordWebMetrics("search", true, searchResults.Count);
            return new WebSearchResult(
                true, query, searchResults, searchResults.Count,
                DurationSeconds: stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "Web搜索失败: {Query}", query);
            RecordWebMetrics("search", false);
            return new WebSearchResult(false, query, [], 0,
                DurationSeconds: stopwatch.Elapsed.TotalSeconds,
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// 构建搜索专用系统提示词 — 对齐 TS prompt.ts getWebSearchPrompt()
    /// </summary>
    private string BuildWebSearchSystemPrompt()
    {
        var currentMonthYear = _clock.GetLocalNow().ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        return $"""
            You are an assistant for performing a web search tool use.

            CRITICAL REQUIREMENT - You MUST follow this:
            - After answering the user's question, you MUST include a "Sources:" section at the end of your response
            - In the Sources section, list all relevant URLs from the search results as markdown hyperlinks: [Title](URL)
            - This is MANDATORY - never skip including sources in your response

            IMPORTANT - Use the correct year in search queries:
            - The current month is {currentMonthYear}. You MUST use this year when searching for recent information.
            """;
    }

    /// <summary>
    /// 从 ApiMessage metadata 提取结构化搜索结果 — 对齐 TS makeOutputFromSearchResponse
    /// </summary>
    private static List<SearchResultItem> ExtractSearchResultsFromMetadata(IReadOnlyList<ApiMessage> messages)
    {
        var results = new List<SearchResultItem>();

        foreach (var msg in messages)
        {
            if (msg.Metadata == null || !msg.Metadata.TryGetValue("web_search_results", out var resultsJson))
                continue;

            if (resultsJson.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var block in resultsJson.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in block.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                    var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;

                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url))
                        continue;

                    // 去重
                    if (results.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    results.Add(new SearchResultItem(title, url));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 构建 web_search 工具 schema（对齐 TS 版 BetaWebSearchTool20250305）
    /// 手动构建 JSON 以兼容 NativeAOT
    /// </summary>
    private static JsonElement BuildWebSearchToolSchema(string[]? allowedDomains, string[]? blockedDomains)
    {
        using var doc = JsonDocument.Parse(BuildWebSearchSchemaJson(allowedDomains, blockedDomains));
        return doc.RootElement.Clone();
    }

    private static string BuildWebSearchSchemaJson(string[]? allowedDomains, string[]? blockedDomains)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"web_search_20250305\",\"name\":\"web_search\",\"max_uses\":8");

        if (allowedDomains is { Length: > 0 })
        {
            sb.Append(",\"allowed_domains\":[");
            sb.Append(string.Join(",", allowedDomains.Select(d => $"\"{JsonEncode(d)}\"")));
            sb.Append(']');
        }

        if (blockedDomains is { Length: > 0 })
        {
            sb.Append(",\"blocked_domains\":[");
            sb.Append(string.Join(",", blockedDomains.Select(d => $"\"{JsonEncode(d)}\"")));
            sb.Append(']');
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string JsonEncode(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// 从文本内容中提取搜索结果链接（回退方案，兼容非 Anthropic provider）
    /// </summary>
    private static void ExtractSearchResults(string content, List<SearchResultItem> results)
    {
        // 匹配 Markdown 链接格式 [title](url)
        var linkRegex = new Regex(@"\[([^\]]+)\]\((https?://[^)]+)\)", RegexOptions.Compiled);
        var matches = linkRegex.Matches(content);

        foreach (Match match in matches)
        {
            var title = match.Groups[1].Value.Trim();
            var url = match.Groups[2].Value.Trim();

            // 去重
            if (results.Any(r => r.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(new SearchResultItem(title, url));
        }
    }

    public async Task<WebFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        var context = new WebContext
        {
            Url = url,
            CancellationToken = cancellationToken
        };

        try
        {
            await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new WebFetchResult(false, url, ErrorMessage: ex.Message);
        }

        return context.Result ?? new WebFetchResult(false, url, ErrorMessage: "Pipeline completed without result");
    }

    /// <summary>
    /// 清理WebFetch缓存（会话清理时调用）
    /// </summary>
    public void ClearCache() => _cache.Clear();

    private void RecordWebMetrics(string operation, bool isSuccess, int size = 0)
        => _telemetryService?.RecordCount("web.operation.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: "Web operation count");
}
