namespace Services.Web;

/// <summary>
/// DeepSeek 网页获取供应商 — POST https://api.deepseek.com/v1/tools/fetch
/// DeepSeek 自研抓取引擎，返回页面内容为 Markdown。API key 通过环境变量 DEEPSEEK_API_KEY 配置，请求头 Authorization: Bearer 传递。
/// 与 <see cref="DeepSeekSearchProvider"/> 共享同一 API Key。
/// </summary>
[Register(typeof(IWebFetchProvider), ServiceLifetime.Singleton)]
public sealed partial class DeepSeekFetchProvider : ServiceEntity, IWebFetchProvider {
    private const string Endpoint = "https://api.deepseek.com/v1/tools/fetch";

    private const string DefaultApiKeyEnvVar = "DEEPSEEK_API_KEY";

    private readonly IApiClient _apiClient;
    private readonly string? _apiKey;
    private readonly ILogger<DeepSeekFetchProvider>? _logger;

    /// <summary>
    /// 初始化 <see cref="DeepSeekFetchProvider"/> 实例。
    /// </summary>
    /// <param name="apiClient">API 客户端，用于执行 HTTP 请求。</param>
    /// <param name="config">统一配置入口，读 searchProviders.deepseek.apiKeyEnvVar（未命中回退 <c>DEEPSEEK_API_KEY</c>）。</param>
    /// <param name="logger">可选日志记录器。</param>
    public DeepSeekFetchProvider(
        IApiClient apiClient,
        IConfigRoot config,
        ILogger<DeepSeekFetchProvider>? logger = null) {
        _apiClient = apiClient;
        var envVarName = SearchProviderConfigHelper.ResolveEnvVar(config, "deepseek", "apiKeyEnvVar", DefaultApiKeyEnvVar);
        _apiKey = Environment.GetEnvironmentVariable(envVarName);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "DeepSeek";

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    /// <inheritdoc />
    public async Task<WebFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default) {
        try {
            var apiKey = _apiKey ?? "";
            if (string.IsNullOrEmpty(apiKey)) {
                return new WebFetchResult(false, url, ErrorMessage: "DEEPSEEK_API_KEY not configured");
            }

            if (string.IsNullOrWhiteSpace(url) || url.Length > 8192) {
                return new WebFetchResult(false, url, ErrorMessage: "URL is empty or exceeds 8192 characters");
            }

            var dto = new DeepSeekFetchRequestDto { Url = url };
            var body = JsonSerializer.Serialize(dto, DeepSeekFetchJsonContext.Default.DeepSeekFetchRequestDto);

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
            var result = JsonSerializer.Deserialize(json, DeepSeekFetchJsonContext.Default.DeepSeekFetchResponseDto);

            var content = result?.Content ?? "";
            if (string.IsNullOrEmpty(content)) {
                return new WebFetchResult(false, url, ErrorMessage: "DeepSeek Fetch returned empty content");
            }

            var title = result?.Title ?? "";
            var fullContent = string.IsNullOrEmpty(title) ? content : $"# {title}\n\n{content}";
            var bytes = Encoding.UTF8.GetByteCount(fullContent);

            return new WebFetchResult(
                true, url,
                Content: fullContent,
                ContentType: "text/markdown",
                Bytes: bytes,
                StatusCode: 200,
                StatusText: "OK");
        } catch (Exception ex) {
            _logger?.LogError(ex, "DeepSeek Fetch 失败: {Url}", url);
            return new WebFetchResult(false, url, ErrorMessage: ex.Message);
        }
    }
}

internal sealed record DeepSeekFetchRequestDto {
    /// <summary>待获取的页面 URL，最多 8192 字符。</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = "";
}

internal sealed record DeepSeekFetchResponseDto {
    /// <summary>页面标题，不可用时为空字符串。</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>页面内容，Markdown 格式。</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeepSeekFetchRequestDto))]
[JsonSerializable(typeof(DeepSeekFetchResponseDto))]
internal sealed partial class DeepSeekFetchJsonContext : JsonSerializerContext;
