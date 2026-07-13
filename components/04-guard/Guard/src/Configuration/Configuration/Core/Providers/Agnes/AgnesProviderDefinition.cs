
namespace Core.Configuration.Providers;

public sealed class AgnesProviderDefinition : IProviderDefinition
{
    private const string ProviderKey = "agnes";

    public string ProviderName => ProviderKind.Agnes.ToValue();
    public string DisplayName => "Agnes";
    public string DefaultModelId => ModelConfigLoader.GetDefaultModelId(ProviderKey);
    public string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId(ProviderKey);
    public string? DefaultEndpoint => "https://apihub.agnes-ai.com/v1";
    public string? ApiKeyEnvironmentVariable => ProviderEnvVar.AgnesApiKey.ToValue();
    public string? EndpointEnvironmentVariable => null;

    public ProviderKind Kind => ProviderKind.Agnes;

    public string GetBaseUrl(ProviderConfig config)
    {
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://apihub.agnes-ai.com/v1/";
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
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AgnesApiKey.ToValue())
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
