
namespace Core.Configuration.Providers;

public sealed class AnthropicProviderDefinition : OpenAICompatibleProviderDefinitionBase
{
    protected override string ProviderConfigKey => "anthropic";
    protected override string DefaultBaseUrl => "https://api.anthropic.com/";
    protected override string ChatCompletionsPath => "v1/messages";
    protected override string AuthHeaderName => "x-api-key";
    protected override string AuthHeaderValuePrefix => "";

    public override ProviderKind Kind => ProviderKind.Anthropic;
    public override string ProviderName => ProviderKind.Anthropic.ToValue();
    public override string DisplayName => "Anthropic";
    public override string DefaultModelId => ModelConfigLoader.GetDefaultModelId("anthropic");
    public override string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId("anthropic");
    public override string? DefaultEndpoint => null;
    public override string? ApiKeyEnvironmentVariable => ProviderEnvVar.AnthropicApiKey.ToValue();
    public override string? EndpointEnvironmentVariable => null;

    public override string GetChatEndpoint(ProviderConfig config) => "v1/messages";

    public override void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
            client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31,prompt-caching-scope-2026-01-05,context-management-2025-06-27");
        }
    }

    public override string? ResolveApiKeyFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AnthropicApiKey.ToValue());
    }

    public override bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey) || config.EnableOAuthTokenSupport;
    }

    public override bool SupportsWebSearch => true;
}
