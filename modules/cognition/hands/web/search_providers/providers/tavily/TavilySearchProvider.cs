namespace Services.Web;

/// <summary>
/// Tavily 搜索引擎供应商 — POST https://api.tavily.com/search
/// AI 搜索优化，返回结构化结果（title/url/content/score）。API key 通过环境变量 TAVILY_API_KEY 配置。
/// 优先级 100（最高优先），需 TAVILY_API_KEY 就绪。
/// </summary>
[Register(typeof(ISearchProvider), ServiceLifetime.Singleton)]
public sealed partial class TavilySearchProvider : ServiceEntity, ISearchProvider {
    private const string Endpoint = "https://api.tavily.com/search";
    private const int DefaultPriority = 100;

    private readonly IApiClient _apiClient;
    private readonly string? _apiKey;
    private readonly ILogger<TavilySearchProvider>? _logger;

    /// <summary>
    /// 初始化 <see cref="TavilySearchProvider"/> 实例。
    /// </summary>
    /// <param name="apiClient">API 客户端，用于执行 HTTP 请求。</param>
    /// <param name="logger">可选日志记录器。</param>
    public TavilySearchProvider(
        IApiClient apiClient,
        ILogger<TavilySearchProvider>? logger = null) {
        _apiClient = apiClient;
        _apiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Tavily";

    /// <inheritdoc />
    public int Priority => DefaultPriority;

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    /// <inheritdoc />
    public async Task<SearchProviderResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) {
        var stopwatch = Stopwatch.StartNew();
        try {
            var apiKey = _apiKey ?? "";
            if (string.IsNullOrEmpty(apiKey)) {
                stopwatch.Stop();
                return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, "TAVILY_API_KEY not configured");
            }

            var dto = new TavilyRequestDto {
                ApiKey = apiKey,
                Query = request.Query,
                MaxResults = request.MaxResults,
                SearchDepth = "basic",
                IncludeAnswer = true,
                IncludeRawContent = false
            };
            var body = JsonSerializer.Serialize(dto, TavilyJsonContext.Default.TavilyRequestDto);

            var apiRequest = ApiRequest.Post(Endpoint, body) with { SkipRetry = true };
            var response = await _apiClient.SendAsync(apiRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize(json, TavilyJsonContext.Default.TavilyResponseDto);

            stopwatch.Stop();
            var items = new List<SearchResultItem>();
            if (result?.Results is not null) {
                foreach (var r in result.Results) {
                    var title = r.Title ?? "";
                    var url = r.Url ?? "";
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url)) continue;
                    var content = r.Content ?? "";
                    items.Add(new SearchResultItem(title, url, Snippet: content.Length > 0 ? content : null));
                }
            }

            return new SearchProviderResult(true, Name, items, stopwatch.Elapsed.TotalSeconds);
        } catch (Exception ex) {
            stopwatch.Stop();
            _logger?.LogError(ex, "Tavily 搜索失败: {Query}", request.Query);
            return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, ex.Message);
        }
    }
}

internal sealed record TavilyRequestDto {
    /// <summary>Tavily API 密钥。</summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }

    /// <summary>搜索查询字符串。</summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = "";

    /// <summary>最大结果数。</summary>
    [JsonPropertyName("max_results")]
    public int MaxResults { get; init; } = 10;

    /// <summary>搜索深度：basic 或 advanced。</summary>
    [JsonPropertyName("search_depth")]
    public string SearchDepth { get; init; } = "basic";

    /// <summary>是否包含 AI 摘要答案。</summary>
    [JsonPropertyName("include_answer")]
    public bool IncludeAnswer { get; init; } = true;

    /// <summary>是否包含原始 HTML 内容。</summary>
    [JsonPropertyName("include_raw_content")]
    public bool IncludeRawContent { get; init; }
}

internal sealed record TavilyResponseDto {
    /// <summary>AI 生成的摘要答案。</summary>
    [JsonPropertyName("answer")]
    public string? Answer { get; init; }

    /// <summary>搜索结果列表。</summary>
    [JsonPropertyName("results")]
    public List<TavilyResultItemDto>? Results { get; init; }
}

internal sealed record TavilyResultItemDto {
    /// <summary>结果标题。</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>结果 URL。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>结果内容摘要。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>相关性评分。</summary>
    [JsonPropertyName("score")]
    public double Score { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TavilyRequestDto))]
[JsonSerializable(typeof(TavilyResponseDto))]
internal sealed partial class TavilyJsonContext : JsonSerializerContext;
