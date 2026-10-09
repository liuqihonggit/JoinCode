// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Configuration.ModelFetch;

/// <summary>
/// 模型列表远程拉取器实现 — 并行请求各供应商 modelsEndpoint，解析返回的模型 id 列表
/// 认证方式根据 protocol 字段决定：openai-compatible 用 Bearer，anthropic 用 x-api-key
/// API Key 优先级：环境变量 > auth.json（按供应商名）
/// 单个供应商失败不影响其他供应商
/// </summary>
public sealed class ModelListFetcher : IModelListFetcher {
    private readonly IHttpClientProvider _httpClientProvider;
    private readonly IFileSystem _fs;
    private readonly ILogger<ModelListFetcher>? _logger;

    /// <summary>
    /// 构造模型列表远程拉取器
    /// </summary>
    /// <param name="httpClientProvider">HTTP 客户端提供者</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">日志器，可为空</param>
    public ModelListFetcher(IHttpClientProvider httpClientProvider, IFileSystem fs, ILogger<ModelListFetcher>? logger = null) {
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
        CancellationToken cancellationToken = default) {
        var authKeys = await LoadAuthKeysAsync(cancellationToken).ConfigureAwait(false);

        var tasks = new List<Task<(string Profile, IReadOnlyList<RemoteModelInfo>? Models)>>(vendor.Count);

        foreach (var (profile, settings) in vendor) {
            if (string.IsNullOrEmpty(settings.Endpoint) || string.IsNullOrEmpty(settings.ModelsEndpoint))
                continue;

            var apiKey = ResolveApiKey(settings, authKeys);
            if (string.IsNullOrEmpty(apiKey)) {
                _logger?.LogWarning("[ModelListFetcher] 跳过 {Profile}：未配置 API Key", profile);
                continue;
            }

            tasks.Add(FetchOneAsync(profile, settings.Endpoint!, settings.ModelsEndpoint!, apiKey, settings.Protocol, cancellationToken));
        }

        if (tasks.Count == 0)
            return FrozenDictionary<string, IReadOnlyList<RemoteModelInfo>>.Empty;

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var dict = new Dictionary<string, IReadOnlyList<RemoteModelInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (profile, models) in results) {
            if (models is not null && models.Count > 0)
                dict[profile] = models;
        }
        return dict;
    }

    private async Task<(string Profile, IReadOnlyList<RemoteModelInfo>? Models)> FetchOneAsync(
        string profile, string endpoint, string modelsEndpoint, string apiKey, string? protocol,
        CancellationToken cancellationToken) {
        try {
            var url = BuildUrl(endpoint, modelsEndpoint);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ConfigureAuth(request, apiKey, protocol);

            var client = _httpClientProvider.GetClient();
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                _logger?.LogWarning("[ModelListFetcher] {Profile} 返回 {Status}，跳过", profile, (int)response.StatusCode);
                return (profile, null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var models = ParseModels(json);
            _logger?.LogInformation("[ModelListFetcher] {Profile} 拉取到 {Count} 个模型", profile, models.Count);
            return (profile, models);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[ModelListFetcher] 拉取 {Profile} 失败，跳过", profile);
            return (profile, null);
        }
    }

    private static string BuildUrl(string endpoint, string modelsEndpoint) {
        var baseUrl = endpoint.TrimEnd('/');
        var relative = modelsEndpoint.Trim('/');
        return $"{baseUrl}/{relative}";
    }

    /// <summary>
    /// 加载 auth.json — 供应商名 → API Key 映射（一次性读取，所有供应商共享）
    /// </summary>
    private async Task<Dictionary<string, string>> LoadAuthKeysAsync(CancellationToken cancellationToken) {
        var settingsPath = SettingsLoader.GetUserSettingsPath();
        var authPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, AppDataConstants.AuthFileName);
        if (!_fs.FileExists(authPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = await DirtyReadRetry.ReadWithRetryAsync(
            () => _fs.ReadAllTextAsync(authPath, cancellationToken),
            json => {
                using var doc = JsonDocument.Parse(json);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in doc.RootElement.EnumerateObject()) {
                    if (prop.Value.ValueKind == JsonValueKind.String) {
                        var value = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(value))
                            dict[prop.Name] = value;
                    }
                }
                return dict;
            },
            authPath,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 API Key — 优先级1: 环境变量，优先级2: auth.json（按供应商名）
    /// </summary>
    private static string? ResolveApiKey(ProfileSettings settings, IReadOnlyDictionary<string, string> authKeys) {
        if (!string.IsNullOrEmpty(settings.ApiKeyEnvVar)) {
            var key = Environment.GetEnvironmentVariable(settings.ApiKeyEnvVar);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        if (!string.IsNullOrEmpty(settings.Provider) && authKeys.TryGetValue(settings.Provider, out var authKey))
            return authKey;
        return null;
    }

    private static void ConfigureAuth(HttpRequestMessage request, string apiKey, string? protocol) {
        if (string.Equals(protocol, ProtocolKindEnumConstants.Anthropic, StringComparison.OrdinalIgnoreCase)) {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2024-10-22");
        } else {
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
        }
    }

    /// <summary>
    /// 解析 OpenAI 兼容格式的模型列表响应 — 提取 id/description/context_length/input_modalities 等完整字段
    /// Anthropic /v1/models 也返回相同格式
    /// </summary>
    private static IReadOnlyList<RemoteModelInfo> ParseModels(string json) {
        try {
            var dto = JsonSerializer.Deserialize(json, ModelFetchJsonContext.Default.ModelListResponseDto);
            if (dto?.Data is null) return Array.Empty<RemoteModelInfo>();

            var list = new List<RemoteModelInfo>();
            foreach (var item in dto.Data) {
                if (string.IsNullOrEmpty(item.Id)) continue;

                list.Add(new RemoteModelInfo {
                    Id = item.Id,
                    Description = item.Description ?? string.Empty,
                    ContextLength = item.ContextLength ?? 0,
                    MaxOutputLength = item.MaxOutputLength ?? 0,
                    InputModalities = FilterNonEmpty(item.InputModalities),
                    OutputModalities = FilterNonEmpty(item.OutputModalities),
                    SupportedFeatures = FilterNonEmpty(item.SupportedFeatures)
                });
            }
            return list;
        } catch {
            return Array.Empty<RemoteModelInfo>();
        }
    }

    /// <summary>
    /// 解析 JSON 对象中的字符串数组属性 — 返回只读列表，属性不存在或非数组时返回空
    /// </summary>
    private static IReadOnlyList<string> ParseStringArray(JsonElement item, string propertyName) {
        if (!item.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<string>();
        foreach (var el in prop.EnumerateArray()) {
            if (el.ValueKind == JsonValueKind.String) {
                var s = el.GetString();
                if (!string.IsNullOrEmpty(s))
                    list.Add(s);
            }
        }
        return list;
    }

    /// <summary>
    /// 过滤字符串数组中的 null 和空字符串 — 返回只读列表，null 输入返回空
    /// </summary>
    private static IReadOnlyList<string> FilterNonEmpty(string[]? source) {
        if (source is null) return [];
        var list = new List<string>(source.Length);
        foreach (var s in source) {
            if (!string.IsNullOrEmpty(s))
                list.Add(s);
        }
        return list;
    }
}

/// <summary>
/// 模型列表响应 DTO — 对应 OpenAI 兼容 /v1/models 响应的顶层结构，用于 DTO 化反序列化替代 TryGetProperty 链式提取
/// </summary>
public sealed class ModelListResponseDto {
    /// <summary>模型数组</summary>
    [JsonPropertyName("data")]
    public ModelItemDto[]? Data { get; set; }
}

/// <summary>
/// 模型项 DTO — 对应 /v1/models 响应 data[] 每一项的结构
/// </summary>
public sealed class ModelItemDto {
    /// <summary>模型 ID — 唯一标识</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>模型描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>上下文窗口长度</summary>
    [JsonPropertyName("context_length")]
    public int? ContextLength { get; set; }

    /// <summary>最大输出长度</summary>
    [JsonPropertyName("max_output_length")]
    public int? MaxOutputLength { get; set; }

    /// <summary>输入模态列表 — 如 ["text","image"]</summary>
    [JsonPropertyName("input_modalities")]
    public string[]? InputModalities { get; set; }

    /// <summary>输出模态列表 — 如 ["text"] 或 ["image"]</summary>
    [JsonPropertyName("output_modalities")]
    public string[]? OutputModalities { get; set; }

    /// <summary>支持的特性列表 — 如 ["tools","json_mode","reasoning"]</summary>
    [JsonPropertyName("supported_features")]
    public string[]? SupportedFeatures { get; set; }
}

/// <summary>
/// 模型拉取 JSON 序列化上下文 — 为模型列表响应 DTO 生成 AOT 兼容的 JSON 序列化代码
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ModelListResponseDto))]
public partial class ModelFetchJsonContext : JsonSerializerContext;