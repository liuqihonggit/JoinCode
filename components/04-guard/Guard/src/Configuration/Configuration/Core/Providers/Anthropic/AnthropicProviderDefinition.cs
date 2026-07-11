
namespace Core.Configuration.Providers;

/// <summary>
/// Anthropic Provider 完整定义
/// </summary>
public sealed class AnthropicProviderDefinition : IProviderDefinition
{
    public string ProviderName => ProviderKind.Anthropic.ToValue();
    public string DisplayName => "Anthropic";
    public string DefaultModelId => "claude-3-5-sonnet-20241022";
    public string DefaultFastModelId => "claude-haiku-4-5-20251001";
    public string? DefaultEndpoint => null;
    public string? ApiKeyEnvironmentVariable => ProviderEnvVar.AnthropicApiKey.ToValue();
    public string? EndpointEnvironmentVariable => null;

    public ProviderKind Kind => ProviderKind.Anthropic;

    public string GetBaseUrl(ProviderConfig config)
    {
        // Anthropic: 自定义 Endpoint 或官方地址
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.anthropic.com/";
    }

    /// <summary>
    /// Anthropic: v1/messages
    /// </summary>
    public string GetChatEndpoint(ProviderConfig config)
    {
        return "v1/messages";
    }

    /// <summary>
    /// Anthropic: x-api-key 认证 + 版本头
    /// </summary>
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
            client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31,prompt-caching-scope-2026-01-05,context-management-2025-06-27");
        }
    }

    public IReadOnlyList<ModelEntry> AvailableModels => AnthropicModels;

    public string? ResolveApiKeyFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AnthropicApiKey.ToValue());
    }

    public bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey) || config.EnableOAuthTokenSupport;
    }

    public string? ResolveAlias(string input)
    {
        return input.ToLowerInvariant() switch
        {
            "sonnet" => CanonicalModel.ClaudeSonnet46.ToValue(),
            "opus" => CanonicalModel.ClaudeOpus46.ToValue(),
            "haiku" => "claude-haiku-4-5-20251001",
            "best" => CanonicalModel.ClaudeOpus46.ToValue(),
            _ => null
        };
    }

    public bool SupportsFastMode(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        // Opus 不支持 Fast Mode
        return !modelId.ToLowerInvariant().Contains("opus");
    }

    public bool SupportsEffort(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        var lower = modelId.ToLowerInvariant();
        // 对齐 TS: 仅 opus-4-6 和 sonnet-4-6 支持 effort
        if (lower.Contains("opus-4-6") || lower.Contains("sonnet-4-6"))
            return true;
        if (lower.Contains("haiku") || lower.Contains("sonnet") || lower.Contains("opus"))
            return false;
        // 未知模型默认支持（对齐 TS firstParty 默认行为）
        return true;
    }

    public bool SupportsMaxEffort(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        // 对齐 TS: 仅 opus-4-6 支持 max
        return modelId.ToLowerInvariant().Contains("opus-4-6");
    }

    /// <summary>
    /// Anthropic 支持服务端 Web 搜索（web_search_20250305）
    /// </summary>
    public bool SupportsWebSearch => true;

    private static readonly ModelEntry[] AnthropicModels =
    [
        new("claude-sonnet-4-6", "Claude Sonnet 4.6", 200_000, "最新 Sonnet，平衡性能与速度"),
        new("claude-opus-4-6", "Claude Opus 4.6", 200_000, "最新 Opus，最强推理能力"),
        new("claude-sonnet-4-5-20250929", "Claude Sonnet 4.5", 200_000, "上一代 Sonnet"),
        new("claude-opus-4-5-20251101", "Claude Opus 4.5", 200_000, "上一代 Opus"),
        new("claude-haiku-4-5-20251001", "Claude Haiku 4.5", 200_000, "快速低成本模型"),
        new("claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet v2", 200_000, "经典 Sonnet v2"),
        new("claude-3-5-haiku-20241022", "Claude 3.5 Haiku", 200_000, "经典 Haiku"),
    ];
}
