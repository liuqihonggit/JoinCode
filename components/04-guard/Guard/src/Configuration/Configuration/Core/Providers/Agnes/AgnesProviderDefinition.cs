
namespace Core.Configuration.Providers;

/// <summary>
/// Agnes AI Provider 完整定义 — OpenAI 兼容协议
/// API Key 优先级: AGNES_API_KEY > OPENAI_API_KEY
/// </summary>
public sealed class AgnesProviderDefinition : IProviderDefinition
{
    public string ProviderName => ProviderKind.Agnes.ToValue();
    public string DisplayName => "Agnes";
    public string DefaultModelId => "agnes-1.5-flash";
    public string DefaultFastModelId => "agnes-1.5-flash";
    public string? DefaultEndpoint => "https://apihub.agnes-ai.com/v1";
    public string? ApiKeyEnvironmentVariable => ProviderEnvVar.AgnesApiKey.ToValue();
    public string? EndpointEnvironmentVariable => null;

    public ProviderKind Kind => ProviderKind.Agnes;

    public string GetBaseUrl(ProviderConfig config)
    {
        // Agnes: 自定义 Endpoint 或默认地址
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://apihub.agnes-ai.com/v1/";
    }

    /// <summary>
    /// Agnes: OpenAI 兼容，如果 Endpoint 已包含 /chat/completions 则不再追加
    /// </summary>
    public string GetChatEndpoint(ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return "chat/completions";
    }

    /// <summary>
    /// Agnes: Bearer Token 认证（OpenAI 兼容）
    /// </summary>
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
    }

    public IReadOnlyList<ModelEntry> AvailableModels => AgnesModels;

    public string? ResolveApiKeyFromEnv()
    {
        // AGNES_API_KEY 优先，回退到 OPENAI_API_KEY（OpenAI 兼容）
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AgnesApiKey.ToValue())
            ?? Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
    }

    public bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    // Agnes 使用自身模型别名
    public string? ResolveAlias(string input)
    {
        return input.ToLowerInvariant() switch
        {
            "flash" => "agnes-1.5-flash",
            "flash2" => "agnes-2.0-flash",
            "image" => "agnes-image-2.0-flash",
            "video" => "agnes-video-v2.0",
            _ => null
        };
    }

    private static readonly ModelEntry[] AgnesModels =
    [
        new("agnes-1.5-flash", "Agnes 1.5 Flash", 128_000, "快速通用模型"),
        new("agnes-2.0-flash", "Agnes 2.0 Flash", 128_000, "新一代快速模型"),
        new("agnes-image-2.0-flash", "Agnes Image 2.0 Flash", 128_000, "图像理解模型"),
        new("agnes-image-2.1-flash", "Agnes Image 2.1 Flash", 128_000, "新一代图像模型"),
        new("agnes-video-v2.0", "Agnes Video 2.0", 128_000, "视频理解模型"),
    ];
}
