namespace Core.Configuration.ModelFetch;

/// <summary>
/// 模型列表远程拉取器实现 — 并行请求各供应商 modelsEndpoint，解析返回的模型 id 列表
/// 认证方式根据 protocol 字段决定：openai-compatible 用 Bearer，anthropic 用 x-api-key
/// API Key 优先级：环境变量 > auth.json（按供应商名）
/// 单个供应商失败不影响其他供应商
/// </summary>
public sealed class ModelListFetcher : IModelListFetcher
{
    private readonly IHttpClientProvider _httpClientProvider;
    private readonly IFileSystem _fs;
    private readonly ILogger<ModelListFetcher>? _logger;

    public ModelListFetcher(IHttpClientProvider httpClientProvider, IFileSystem fs, ILogger<ModelListFetcher>? logger = null)
    {
        _httpClientProvider = httpClientProvider;
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// 并行拉取所有已配置 modelsEndpoint 的供应商的模型列表
    /// 跳过条件：endpoint 为空、modelsEndpoint 为空、API Key 未配置
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RemoteModelInfo>>> FetchAllAsync(
        IReadOnlyDictionary<string, ProfileSettings> vendor,
        CancellationToken cancellationToken = default)
    {
        var authKeys = await LoadAuthKeysAsync(cancellationToken).ConfigureAwait(false);

        var tasks = new List<Task<(string Profile, IReadOnlyList<RemoteModelInfo>? Models)>>(vendor.Count);

        foreach (var (profile, settings) in vendor)
        {
            if (string.IsNullOrEmpty(settings.Endpoint) || string.IsNullOrEmpty(settings.ModelsEndpoint))
                continue;

            var apiKey = ResolveApiKey(settings, authKeys);
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger?.LogWarning("[ModelListFetcher] 跳过 {Profile}：未配置 API Key", profile);
                continue;
            }

            tasks.Add(FetchOneAsync(profile, settings.Endpoint!, settings.ModelsEndpoint!, apiKey, settings.Protocol, cancellationToken));
        }

        if (tasks.Count == 0)
            return FrozenDictionary<string, IReadOnlyList<RemoteModelInfo>>.Empty;

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var dict = new Dictionary<string, IReadOnlyList<RemoteModelInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (profile, models) in results)
        {
            if (models is not null && models.Count > 0)
                dict[profile] = models;
        }
        return dict;
    }

    private async Task<(string Profile, IReadOnlyList<RemoteModelInfo>? Models)> FetchOneAsync(
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
            var models = ParseModels(json);
            _logger?.LogInformation("[ModelListFetcher] {Profile} 拉取到 {Count} 个模型", profile, models.Count);
            return (profile, models);
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

    /// <summary>
    /// 加载 auth.json — 供应商名 → API Key 映射（一次性读取，所有供应商共享）
    /// </summary>
    private async Task<Dictionary<string, string>> LoadAuthKeysAsync(CancellationToken cancellationToken)
    {
        var settingsPath = SettingsLoader.GetUserSettingsPath();
        var authPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, AppDataConstants.AuthFileName);
        if (!_fs.FileExists(authPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = await _fs.ReadAllTextAsync(authPath, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(value))
                        dict[prop.Name] = value;
                }
            }
            return dict;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 解析 API Key — 优先级1: 环境变量，优先级2: auth.json（按供应商名）
    /// </summary>
    private static string? ResolveApiKey(ProfileSettings settings, IReadOnlyDictionary<string, string> authKeys)
    {
        if (!string.IsNullOrEmpty(settings.ApiKeyEnvVar))
        {
            var key = Environment.GetEnvironmentVariable(settings.ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        if (!string.IsNullOrEmpty(settings.Provider) && authKeys.TryGetValue(settings.Provider, out var authKey))
            return authKey;
        return null;
    }

    private static void ConfigureAuth(HttpRequestMessage request, string apiKey, string? protocol)
    {
        if (string.Equals(protocol, ProtocolKindConstants.Anthropic, StringComparison.OrdinalIgnoreCase))
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
    /// 解析 OpenAI 兼容格式的模型列表响应 — 提取 id/description/context_length/input_modalities 等完整字段
    /// Anthropic /v1/models 也返回相同格式
    /// </summary>
    private static IReadOnlyList<RemoteModelInfo> ParseModels(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Array.Empty<RemoteModelInfo>();

            var list = new List<RemoteModelInfo>();
            foreach (var item in data.EnumerateArray())
            {
                var info = new RemoteModelInfo();
                if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    info.Id = idProp.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(info.Id))
                    continue;

                if (item.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String)
                    info.Description = descProp.GetString() ?? string.Empty;

                if (item.TryGetProperty("context_length", out var ctxProp) && ctxProp.ValueKind == JsonValueKind.Number)
                    info.ContextLength = ctxProp.GetInt32();

                if (item.TryGetProperty("max_output_length", out var maxOutProp) && maxOutProp.ValueKind == JsonValueKind.Number)
                    info.MaxOutputLength = maxOutProp.GetInt32();

                info.InputModalities = ParseStringArray(item, "input_modalities");
                info.OutputModalities = ParseStringArray(item, "output_modalities");
                info.SupportedFeatures = ParseStringArray(item, "supported_features");

                list.Add(info);
            }
            return list;
        }
        catch
        {
            return Array.Empty<RemoteModelInfo>();
        }
    }

    /// <summary>
    /// 解析 JSON 对象中的字符串数组属性 — 返回只读列表，属性不存在或非数组时返回空
    /// </summary>
    private static IReadOnlyList<string> ParseStringArray(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<string>();
        foreach (var el in prop.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrEmpty(s))
                    list.Add(s);
            }
        }
        return list;
    }
}
