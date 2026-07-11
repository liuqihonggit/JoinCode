namespace Core.Configuration.Providers;

/// <summary>
/// DeepSeek Provider 完整定义 — OpenAI 兼容协议
///
/// 协议特性:
/// - 端点路径: /chat/completions（无 /v1 前缀，与 OpenAI 的 /v1/chat/completions 不同）
/// - 认证: Bearer Token（OpenAI 兼容）
/// - 缓存统计: prompt_cache_hit_tokens + prompt_cache_miss_tokens
///   由 OpenAIQueryService 已支持解析（PromptCacheHitTokens/PromptCacheMissTokens 字段）
///
/// API Key 优先级: DEEPSEEK_API_KEY > JCC_API_KEY
/// </summary>
public sealed class DeepSeekProviderDefinition : IProviderDefinition
{
    public string ProviderName => ProviderKind.DeepSeek.ToValue();
    public string DisplayName => "DeepSeek";
    public string DefaultModelId => "deepseek-chat";
    public string DefaultFastModelId => "deepseek-chat";
    public string? DefaultEndpoint => "https://api.deepseek.com";
    public string? ApiKeyEnvironmentVariable => ProviderEnvVar.DeepSeekApiKey.ToValue();
    public string? EndpointEnvironmentVariable => null;

    public ProviderKind Kind => ProviderKind.DeepSeek;

    public string GetBaseUrl(ProviderConfig config)
    {
        // DeepSeek: 自定义 Endpoint 或官方地址（无 /v1 前缀，与 OpenAI 不同）
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.deepseek.com/";
    }

    /// <summary>
    /// DeepSeek: OpenAI 兼容，如果 Endpoint 已包含 /chat/completions 则不再追加
    /// </summary>
    public string GetChatEndpoint(ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return "chat/completions";
    }

    /// <summary>
    /// DeepSeek: Bearer Token 认证（OpenAI 兼容）
    /// </summary>
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
    }

    public IReadOnlyList<ModelEntry> AvailableModels => DeepSeekModels;

    public string? ResolveApiKeyFromEnv()
    {
        // DEEPSEEK_API_KEY 优先；OpenAI 兼容，回退到 OPENAI_API_KEY
        return Environment.GetEnvironmentVariable(ProviderEnvVar.DeepSeekApiKey.ToValue())
            ?? Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
    }

    public bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    /// <summary>
    /// DeepSeek 模型别名解析
    /// </summary>
    public string? ResolveAlias(string input)
    {
        return input.ToLowerInvariant() switch
        {
            "chat" => "deepseek-chat",
            "reasoner" => "deepseek-reasoner",
            "r1" => "deepseek-reasoner",
            "v3" => "deepseek-chat",
            _ => null
        };
    }

    private static readonly ModelEntry[] DeepSeekModels =
    [
        new("deepseek-chat", "DeepSeek V3", 128_000, "通用对话模型（V3）"),
        new("deepseek-reasoner", "DeepSeek R1", 128_000, "推理模型（R1）"),
    ];
}
