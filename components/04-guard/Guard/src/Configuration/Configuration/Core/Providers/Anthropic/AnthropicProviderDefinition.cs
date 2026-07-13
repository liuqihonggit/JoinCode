
namespace Core.Configuration.Providers;

public sealed class AnthropicProviderDefinition : IProviderDefinition
{
    private const string ProviderKey = "anthropic";

    public string ProviderName => ProviderKind.Anthropic.ToValue();
    public string DisplayName => "Anthropic";
    public string DefaultModelId => ModelConfigLoader.GetDefaultModelId(ProviderKey);
    public string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId(ProviderKey);
    public string? DefaultEndpoint => null;
    public string? ApiKeyEnvironmentVariable => ProviderEnvVar.AnthropicApiKey.ToValue();
    public string? EndpointEnvironmentVariable => null;

    public ProviderKind Kind => ProviderKind.Anthropic;

    public string GetBaseUrl(ProviderConfig config)
    {
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.anthropic.com/";
    }

    public string GetChatEndpoint(ProviderConfig config)
    {
        return "v1/messages";
    }

    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
            client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31,prompt-caching-scope-2026-01-05,context-management-2025-06-27");
        }
    }

    public IReadOnlyList<ModelEntry> AvailableModels => ModelConfigLoader.GetModels(ProviderKey);

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
        return ModelConfigLoader.ResolveAlias(ProviderKey, input);
    }

    public bool SupportsFastMode(string modelId)
    {
        return ModelConfigLoader.SupportsFastMode(ProviderKey, modelId);
    }

    public bool SupportsEffort(string modelId)
    {
        return ModelConfigLoader.SupportsEffort(ProviderKey, modelId);
    }

    public bool SupportsMaxEffort(string modelId)
    {
        return ModelConfigLoader.SupportsMaxEffort(ProviderKey, modelId);
    }

    public bool SupportsWebSearch => true;
}
