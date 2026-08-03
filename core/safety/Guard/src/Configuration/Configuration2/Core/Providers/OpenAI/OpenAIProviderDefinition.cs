
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

    public override void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        base.ConfigureHttpClient(client, config);
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
