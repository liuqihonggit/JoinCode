
namespace Core.Configuration.Providers;

public sealed class OpenAIProviderDefinition : OpenAICompatibleProviderDefinitionBase
{
    public override ProviderKind Kind => ProviderKind.OpenAI;
    public override string ProviderName => ProviderKind.OpenAI.ToValue();
    public override string DisplayName => "OpenAI";
    public override string DefaultModelId => ModelConfigLoader.GetDefaultModelId("openai");
    public override string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId("openai");
    public override string? DefaultEndpoint => null;
    public override string? ApiKeyEnvironmentVariable => ProviderEnvVar.OpenAiApiKey.ToValue();
    public override string? EndpointEnvironmentVariable => null;

    public override string GetBaseUrl(ProviderConfig config)
    {
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.openai.com/v1/";
    }

    public override string GetChatEndpoint(ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return "chat/completions";
    }

    public override void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
        if (!string.IsNullOrEmpty(config.OrganizationId))
            client.DefaultRequestHeaders.Add("OpenAI-Organization", config.OrganizationId);
    }

    public override string? ResolveApiKeyFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
    }

    public override bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey) || config.EnableOAuthTokenSupport;
    }
}
