namespace Services.Web;

/// <summary>
/// SearXNG 搜索引擎供应商 — GET {SEARXNG_URL}/search?format=json
/// 自托管元搜索引擎，聚合 Google/Bing/DuckDuckGo 等多引擎结果。无 API key，需配置 SEARXNG_URL 环境变量指向实例地址。
/// 优先级 400，需 SEARXNG_URL 就绪。
/// </summary>
[Register(typeof(ISearchProvider), ServiceLifetime.Singleton)]
public sealed partial class SearXngSearchProvider : ServiceEntity, ISearchProvider {
    private const int DefaultPriority = 400;
    private const string DefaultEndpointEnvVar = "SEARXNG_URL";

    private readonly IApiClient _apiClient;
    private readonly string? _baseUrl;
    private readonly ILogger<SearXngSearchProvider>? _logger;

    /// <summary>
    /// 初始化 <see cref="SearXngSearchProvider"/> 实例。
    /// </summary>
    /// <param name="apiClient">API 客户端，用于执行 HTTP 请求。</param>
    /// <param name="config">统一配置入口，读 searchProviders.searxng.endpointEnvVar（未命中回退 <c>SEARXNG_URL</c>）。</param>
    /// <param name="logger">可选日志记录器。</param>
    public SearXngSearchProvider(
        IApiClient apiClient,
        IConfigRoot config,
        ILogger<SearXngSearchProvider>? logger = null) {
        _apiClient = apiClient;
        var envVarName = SearchProviderConfigHelper.ResolveEnvVar(config, "searxng", "endpointEnvVar", DefaultEndpointEnvVar);
        _baseUrl = Environment.GetEnvironmentVariable(envVarName)?.TrimEnd('/');
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SearXNG";

    /// <inheritdoc />
    public int Priority => DefaultPriority;

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_baseUrl) && Uri.TryCreate(_baseUrl, UriKind.Absolute, out _);

    /// <inheritdoc />
    public async Task<SearchProviderResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) {
        var stopwatch = Stopwatch.StartNew();
        try {
            var baseUrl = _baseUrl ?? "";
            if (string.IsNullOrEmpty(baseUrl)) {
                stopwatch.Stop();
                return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, "SEARXNG_URL not configured");
            }

            var queryParams = new Dictionary<string, string> {
                ["q"] = request.Query,
                ["format"] = "json",
                ["pageno"] = "1"
            };
            var apiRequest = new ApiRequest {
                Method = HttpMethod.Get,
                Path = $"{baseUrl}/search",
                QueryParams = queryParams,
                SkipRetry = true
            };
            var response = await _apiClient.SendAsync(apiRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize(json, SearXngJsonContext.Default.SearXngResponseDto);

            stopwatch.Stop();
            var items = new List<SearchResultItem>();
            if (result?.Results is not null) {
                foreach (var r in result.Results) {
                    var title = r.Title ?? "";
                    var url = r.Url ?? "";
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url)) continue;
                    var content = r.Content ?? "";
                    items.Add(new SearchResultItem(title, url, Snippet: content.Length > 0 ? content : null));
                    if (items.Count >= request.MaxResults) break;
                }
            }

            return new SearchProviderResult(true, Name, items, stopwatch.Elapsed.TotalSeconds);
        } catch (Exception ex) {
            stopwatch.Stop();
            _logger?.LogError(ex, "SearXNG 搜索失败: {Query}", request.Query);
            return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, ex.Message);
        }
    }
}

internal sealed record SearXngResponseDto {
    /// <summary>搜索结果列表。</summary>
    [JsonPropertyName("results")]
    public List<SearXngResultItemDto>? Results { get; init; }
}

internal sealed record SearXngResultItemDto {
    /// <summary>结果标题。</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>结果 URL。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>结果内容摘要。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>来源搜索引擎。</summary>
    [JsonPropertyName("engine")]
    public string? Engine { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SearXngResponseDto))]
internal sealed partial class SearXngJsonContext : JsonSerializerContext;
