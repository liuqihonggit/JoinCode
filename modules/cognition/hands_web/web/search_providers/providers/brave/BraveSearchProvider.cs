namespace Services.Web;

/// <summary>
/// Brave 搜索引擎供应商 — GET https://api.search.brave.com/res/v1/web/search
/// 隐私优先，独立索引。API key 通过环境变量 BRAVE_API_KEY 配置，请求头 X-Subscription-Token 传递。
/// 优先级 300，需 BRAVE_API_KEY 就绪。
/// </summary>
[Register(typeof(ISearchProvider), ServiceLifetime.Singleton)]
public sealed partial class BraveSearchProvider : ServiceEntity, ISearchProvider {
    private const string Endpoint = "https://api.search.brave.com/res/v1/web/search";
    private const int DefaultPriority = 300;

    private const string DefaultApiKeyEnvVar = "BRAVE_API_KEY";

    private readonly IApiClient _apiClient;
    private readonly string? _apiKey;
    private readonly ILogger<BraveSearchProvider>? _logger;

    /// <summary>
    /// 初始化 <see cref="BraveSearchProvider"/> 实例。
    /// </summary>
    /// <param name="apiClient">API 客户端，用于执行 HTTP 请求。</param>
    /// <param name="config">统一配置入口，读 searchProviders.brave.apiKeyEnvVar（未命中回退 <c>BRAVE_API_KEY</c>）。</param>
    /// <param name="logger">可选日志记录器。</param>
    public BraveSearchProvider(
        IApiClient apiClient,
        IConfigRoot config,
        ILogger<BraveSearchProvider>? logger = null) {
        _apiClient = apiClient;
        var envVarName = SearchProviderConfigHelper.ResolveEnvVar(config, "brave", "apiKeyEnvVar", DefaultApiKeyEnvVar);
        _apiKey = Environment.GetEnvironmentVariable(envVarName);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Brave";

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
                return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, "BRAVE_API_KEY not configured");
            }

            var queryParams = new Dictionary<string, string> {
                ["q"] = request.Query,
                ["count"] = request.MaxResults.ToString(CultureInfo.InvariantCulture)
            };
            var apiRequest = new ApiRequest {
                Method = HttpMethod.Get,
                Path = Endpoint,
                QueryParams = queryParams,
                Headers = new Dictionary<string, string> { ["X-Subscription-Token"] = apiKey },
                SkipRetry = true
            };
            var response = await _apiClient.SendAsync(apiRequest, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize(json, BraveJsonContext.Default.BraveResponseDto);

            stopwatch.Stop();
            var items = new List<SearchResultItem>();
            if (result?.Web?.Results is not null) {
                foreach (var r in result.Web.Results) {
                    var title = r.Title ?? "";
                    var url = r.Url ?? "";
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(url)) continue;
                    var description = r.Description ?? "";
                    items.Add(new SearchResultItem(title, url, Snippet: description.Length > 0 ? description : null));
                }
            }

            return new SearchProviderResult(true, Name, items, stopwatch.Elapsed.TotalSeconds);
        } catch (Exception ex) {
            stopwatch.Stop();
            _logger?.LogError(ex, "Brave 搜索失败: {Query}", request.Query);
            return new SearchProviderResult(false, Name, [], stopwatch.Elapsed.TotalSeconds, ex.Message);
        }
    }
}

internal sealed record BraveResponseDto {
    /// <summary>Web 搜索结果容器。</summary>
    [JsonPropertyName("web")]
    public BraveWebDto? Web { get; init; }
}

internal sealed record BraveWebDto {
    /// <summary>搜索结果列表。</summary>
    [JsonPropertyName("results")]
    public List<BraveResultItemDto>? Results { get; init; }
}

internal sealed record BraveResultItemDto {
    /// <summary>结果标题。</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>结果 URL。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>结果描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BraveResponseDto))]
internal sealed partial class BraveJsonContext : JsonSerializerContext;
