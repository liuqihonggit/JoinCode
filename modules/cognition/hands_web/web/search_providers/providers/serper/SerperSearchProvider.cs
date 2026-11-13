namespace Services.Web;

/// <summary>
/// Serper 搜索引擎供应商 — POST https://google.serper.dev/search
/// 返回 Google 搜索结果（title/link/snippet）。API key 通过环境变量 SERPER_API_KEY 配置，请求头 X-API-KEY 传递。
/// 优先级 200，需 SERPER_API_KEY 就绪。
/// </summary>
[Register(typeof(ISearchProvider), ServiceLifetime.Singleton)]
public sealed partial class SerperSearchProvider : SearchProviderBase {
    private const string Endpoint = "https://google.serper.dev/search";
    private const int DefaultPriority = 200;

    private const string DefaultApiKeyEnvVar = "SERPER_API_KEY";

    private readonly IApiClient _apiClient;
    private readonly string? _apiKey;

    /// <summary>
    /// 初始化 <see cref="SerperSearchProvider"/> 实例。
    /// </summary>
    /// <param name="apiClient">API 客户端，用于执行 HTTP 请求。</param>
    /// <param name="config">统一配置入口，读 searchProviders.serper.apiKeyEnvVar（未命中回退 <c>SERPER_API_KEY</c>）。</param>
    /// <param name="logger">可选日志记录器。</param>
    public SerperSearchProvider(
        IApiClient apiClient,
        IConfigRoot config,
        ILogger<SerperSearchProvider>? logger = null)
        : base(logger) {
        _apiClient = apiClient;
        var envVarName = SearchProviderConfigHelper.ResolveEnvVar(config, "serper", "apiKeyEnvVar", DefaultApiKeyEnvVar);
        _apiKey = Environment.GetEnvironmentVariable(envVarName);
    }

    /// <inheritdoc />
    public override string Name => "Serper";

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    /// <inheritdoc />
    protected override async Task<SearchProviderResult> SearchCoreAsync(SearchRequest request, CancellationToken cancellationToken) {
        var apiKey = _apiKey ?? "";
        if (string.IsNullOrEmpty(apiKey)) {
            return new SearchProviderResult(false, Name, [], 0, "SERPER_API_KEY not configured");
        }

        var dto = new SerperRequestDto {
            Query = request.Query,
            Num = request.MaxResults
        };
        var body = JsonSerializer.Serialize(dto, SerperJsonContext.Default.SerperRequestDto);

        var apiRequest = new ApiRequest {
            Method = HttpMethod.Post,
            Path = Endpoint,
            Body = body,
            Headers = new Dictionary<string, string> { ["X-API-KEY"] = apiKey },
            SkipRetry = true
        };
        var response = await _apiClient.SendAsync(apiRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize(json, SerperJsonContext.Default.SerperResponseDto);

        var items = new List<SearchResultItem>();
        if (result?.Organic is not null) {
            foreach (var r in result.Organic) {
                var title = r.Title ?? "";
                var link = r.Link ?? "";
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link)) continue;
                var snippet = r.Snippet ?? "";
                items.Add(new SearchResultItem(title, link, Snippet: snippet.Length > 0 ? snippet : null));
            }
        }

        return new SearchProviderResult(true, Name, items);
    }
}

internal sealed record SerperRequestDto {
    /// <summary>搜索查询字符串。</summary>
    [JsonPropertyName("q")]
    public string Query { get; init; } = "";

    /// <summary>最大结果数。</summary>
    [JsonPropertyName("num")]
    public int Num { get; init; } = 10;
}

internal sealed record SerperResponseDto {
    /// <summary>自然搜索结果列表。</summary>
    [JsonPropertyName("organic")]
    public List<SerperResultItemDto>? Organic { get; init; }
}

internal sealed record SerperResultItemDto {
    /// <summary>结果标题。</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>结果链接。</summary>
    [JsonPropertyName("link")]
    public string? Link { get; init; }

    /// <summary>结果摘要。</summary>
    [JsonPropertyName("snippet")]
    public string? Snippet { get; init; }

    /// <summary>结果位置。</summary>
    [JsonPropertyName("position")]
    public int Position { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SerperRequestDto))]
[JsonSerializable(typeof(SerperResponseDto))]
internal sealed partial class SerperJsonContext : JsonSerializerContext;
