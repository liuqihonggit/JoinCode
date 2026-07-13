
namespace Core.Configuration.Providers;

public sealed class DeepSeekProviderDefinition : IProviderDefinition
{
    private const string ProviderKey = "deepseek";

    public string ProviderName => ProviderKind.DeepSeek.ToValue();
    public string DisplayName => "DeepSeek";
    public string DefaultModelId => ModelConfigLoader.GetDefaultModelId(ProviderKey);
    public string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId(ProviderKey);
    public string? DefaultEndpoint => "https://api.deepseek.com";
    public string? ApiKeyEnvironmentVariable => ProviderEnvVar.DeepSeekApiKey.ToValue();
    public string? EndpointEnvironmentVariable => null;

    public ProviderKind Kind => ProviderKind.DeepSeek;

    public string GetBaseUrl(ProviderConfig config)
    {
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.deepseek.com/";
    }

    public string GetChatEndpoint(ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return "chat/completions";
    }

    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
    }

    public IReadOnlyList<ModelEntry> AvailableModels => ModelConfigLoader.GetModels(ProviderKey);

    public string? ResolveApiKeyFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.DeepSeekApiKey.ToValue())
            ?? Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
    }

    public bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey);
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
}
