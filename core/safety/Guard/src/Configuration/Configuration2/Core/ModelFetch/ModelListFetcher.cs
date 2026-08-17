namespace Core.Configuration.ModelFetch;

/// <summary>
/// 模型列表远程拉取器实现 — 并行请求各供应商 modelsEndpoint，解析返回的模型 id 列表
/// 认证方式根据 protocol 字段决定：openai-compatible 用 Bearer，anthropic 用 x-api-key
/// 单个供应商失败不影响其他供应商
/// </summary>
public sealed class ModelListFetcher : IModelListFetcher
{
    private readonly IHttpClientProvider _httpClientProvider;
    private readonly ILogger<ModelListFetcher>? _logger;

    public ModelListFetcher(IHttpClientProvider httpClientProvider, ILogger<ModelListFetcher>? logger = null)
    {
        _httpClientProvider = httpClientProvider;
        _logger = logger;
    }

    /// <summary>
    /// 并行拉取所有已配置 modelsEndpoint 的供应商的模型列表
    /// 跳过条件：endpoint 为空、modelsEndpoint 为空、API Key 未配置
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FetchAllAsync(
        IReadOnlyDictionary<string, ProfileSettings> vendor,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<(string Profile, IReadOnlyList<string>? Models)>>(vendor.Count);

        foreach (var (profile, settings) in vendor)
        {
            if (string.IsNullOrEmpty(settings.Endpoint) || string.IsNullOrEmpty(settings.ModelsEndpoint))
                continue;

            var apiKey = ResolveApiKey(settings);
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger?.LogWarning("[ModelListFetcher] 跳过 {Profile}：未配置 API Key", profile);
                continue;
            }

            tasks.Add(FetchOneAsync(profile, settings.Endpoint!, settings.ModelsEndpoint!, apiKey, settings.Protocol, cancellationToken));
        }

        if (tasks.Count == 0)
            return FrozenDictionary<string, IReadOnlyList<string>>.Empty;

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var dict = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (profile, models) in results)
        {
            if (models is not null && models.Count > 0)
                dict[profile] = models;
        }
        return dict;
    }

    private async Task<(string Profile, IReadOnlyList<string>? Models)> FetchOneAsync(
        string profile, string endpoint, string modelsEndpoint, string apiKey, string? protocol,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = BuildUrl(endpoint, modelsEndpoint);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ConfigureAuth(request, apiKey, protocol);

            var client = _httpClientProvider.GetClient();
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("[ModelListFetcher] {Profile} 返回 {Status}，跳过", profile, (int)response.StatusCode);
                return (profile, null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var ids = ParseModelIds(json);
            _logger?.LogInformation("[ModelListFetcher] {Profile} 拉取到 {Count} 个模型", profile, ids.Count);
            return (profile, ids);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ModelListFetcher] 拉取 {Profile} 失败，跳过", profile);
            return (profile, null);
        }
    }

    private static string BuildUrl(string endpoint, string modelsEndpoint)
    {
        var baseUrl = endpoint.TrimEnd('/');
        var relative = modelsEndpoint.Trim('/');
        return $"{baseUrl}/{relative}";
    }

    private static string? ResolveApiKey(ProfileSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.ApiKeyEnvVar))
        {
            var key = Environment.GetEnvironmentVariable(settings.ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        return null;
    }

    private static void ConfigureAuth(HttpRequestMessage request, string apiKey, string? protocol)
    {
        if (string.Equals(protocol, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2024-10-22");
        }
        else
        {
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
        }
    }

    /// <summary>
    /// 解析 OpenAI 兼容格式的模型列表响应 — { "data": [{ "id": "..." }] }
    /// Anthropic /v1/models 也返回相同格式
    /// </summary>
    private static IReadOnlyList<string> ParseModelIds(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var ids = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id))
                        ids.Add(id);
                }
            }
            return ids;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
