namespace Services.Web;

/// <summary>
/// DeepSeek 搜索引擎供应商 — POST https://api.deepseek.com/v1/tools/search
/// DeepSeek 自研搜索引擎，返回结构化结果（title/url/snippet/summary/content/rerank_score/authority/date）。
/// API key 通过环境变量 DEEPSEEK_API_KEY 配置，请求头 Authorization: Bearer 传递。
/// 优先级 90（高于 Tavily=100），需 DEEPSEEK_API_KEY 就绪。
/// </summary>
[Register(typeof(ISearchProvider), ServiceLifetime.Singleton)]
public sealed partial class DeepSeekSearchProvider : ServiceEntity, ISearchProvider {
    private const string Endpoint = "https://api.deepseek.com/v1/tools/search";
    private const int DefaultPriority = 90;

    private const string DefaultApiKeyEnvVar = "DEEPSEEK_API_KEY";

    private readonly IApiClient _apiClient;
    private readonly string? _apiKey;
    private readonly ILogger<DeepSeekSearchProvider>? _logger;

    /// <summary>
    /// 初始化 <see cref="DeepSeekSearchProvider"/> 实例。
    /// </summary>
    /// <param name="apiClient">API 客户端，用于执行 HTTP 请求。</param>
    /// <param name="config">统一配置入口，读 searchProviders.deepseek.apiKeyEnvVar（未命中回退 <c>DEEPSEEK_API_KEY</c>）。</param>
    /// <param name="logger">可选日志记录器。</param>
    public DeepSeekSearchProvider(
        IApiClient apiClient,
        IConfigRoot config,
        ILogger<DeepSeekSearchProvider>? logger = null) {
        _apiClient = apiClient;
        var envVarName = SearchProviderConfigHelper.ResolveEnvVar(config, "deepseek", "apiKeyEnvVar", DefaultApiKeyEnvVar);
        _apiKey = Environment.GetEnvironmentVariable(envVarName);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "DeepSeek";

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
                return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, "DEEPSEEK_API_KEY not configured");
            }

            var query = request.Query.Length > 128 ? request.Query[..128] : request.Query;
            var dto = new DeepSeekSearchRequestDto {
                Query = query,
                Count = Math.Clamp(request.MaxResults, 1, 50),
                NeedContent = false
            };
            var body = JsonSerializer.Serialize(dto, DeepSeekSearchJsonContext.Default.DeepSeekSearchRequestDto);

            var apiRequest = new ApiRequest {
                Method = HttpMethod.Post,
                Path = Endpoint,
                Body = body,
                Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" },
                SkipRetry = true
            };
            var response = await _apiClient.SendAsync(apiRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize(json, DeepSeekSearchJsonContext.Default.DeepSeekSearchResponseDto);

            stopwatch.Stop();
            var items = new List<SearchResultItem>();
            if (result?.Results is not null) {
                foreach (var r in result.Results) {
                    var title = r.Title ?? "";
                    var url = r.Url ?? "";
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url)) continue;
                    var snippet = r.Summary ?? r.Snippet ?? "";
                    DateTime? publishedDate = null;
                    if (!string.IsNullOrEmpty(r.Date) && DateTime.TryParse(r.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)) {
                        publishedDate = dt;
                    }
                    items.Add(new SearchResultItem(title, url, Snippet: snippet.Length > 0 ? snippet : null, PublishedDate: publishedDate));
                }
            }

            return new SearchProviderResult(true, Name, items, stopwatch.Elapsed.TotalSeconds);
        } catch (Exception ex) {
            stopwatch.Stop();
            _logger?.LogError(ex, "DeepSeek 搜索失败: {Query}", request.Query);
            return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, ex.Message);
        }
    }
}

internal sealed record DeepSeekSearchRequestDto {
    /// <summary>搜索查询字符串，1-128 Unicode 字符。</summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = "";

    /// <summary>返回结果数，1-50，默认 10。</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; } = 10;

    /// <summary>是否返回页面内容，false 时 content 字段为空字符串。</summary>
    [JsonPropertyName("need_content")]
    public bool NeedContent { get; init; }
}

internal sealed record DeepSeekSearchResponseDto {
    /// <summary>搜索结果列表，无匹配时为空数组。</summary>
    [JsonPropertyName("results")]
    public List<DeepSeekSearchResultItemDto>? Results { get; init; }
}

internal sealed record DeepSeekSearchResultItemDto {
    /// <summary>相关性评分，0-3，≥1.5 为高质量结果。</summary>
    [JsonPropertyName("rerank_score")]
    public double RerankScore { get; init; }

    /// <summary>结果标题。</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>结果 URL。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>短摘要，100-200 字符，适合 UI 展示。</summary>
    [JsonPropertyName("snippet")]
    public string? Snippet { get; init; }

    /// <summary>长摘要，600-800 字符，适合模型阅读。</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    /// <summary>页面内容，need_content=false 时为空。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>站点名称，不可用时为 null。</summary>
    [JsonPropertyName("site_name")]
    public string? SiteName { get; init; }

    /// <summary>站点图标 URL，不可用时为 null。</summary>
    [JsonPropertyName("site_icon")]
    public string? SiteIcon { get; init; }

    /// <summary>发布时间，RFC 3339 格式 UTC，未知时为 null。</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    /// <summary>权威度等级，0-4，-1 为未知，≥3 为高权威源。</summary>
    [JsonPropertyName("authority")]
    public int Authority { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeepSeekSearchRequestDto))]
[JsonSerializable(typeof(DeepSeekSearchResponseDto))]
internal sealed partial class DeepSeekSearchJsonContext : JsonSerializerContext;
