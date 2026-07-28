
namespace Core.Configuration.Providers;

public abstract class OpenAICompatibleProviderDefinitionBase : IProviderDefinition
{
    protected virtual string ProviderConfigKey => "openai";

    public abstract ProviderKind Kind { get; }
    public abstract string ProviderName { get; }
    public abstract string DisplayName { get; }
    public abstract string DefaultModelId { get; }
    public abstract string DefaultFastModelId { get; }
    public abstract string? DefaultEndpoint { get; }
    public abstract string? ApiKeyEnvironmentVariable { get; }
    public abstract string? EndpointEnvironmentVariable { get; }

    protected virtual string DefaultBaseUrl => "https://api.openai.com/v1/";
    protected virtual string ChatCompletionsPath => "chat/completions";
    protected virtual string AuthHeaderName => "Authorization";
    protected virtual string AuthHeaderValuePrefix => "Bearer ";

    public virtual string GetBaseUrl(ProviderConfig config)
    {
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : DefaultBaseUrl;
    }

    public virtual string GetChatEndpoint(ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith(ChatCompletionsPath, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return ChatCompletionsPath;
    }

    public virtual void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add(AuthHeaderName, $"{AuthHeaderValuePrefix}{config.ApiKey}");
    }

    public abstract string? ResolveApiKeyFromEnv();

    public virtual bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    public virtual IReadOnlyList<ModelEntry> AvailableModels => ModelConfigLoader.GetModels(ProviderConfigKey);

    public virtual string? ResolveAlias(string input)
    {
        return ModelConfigLoader.ResolveAlias(ProviderConfigKey, input);
    }

    public virtual bool SupportsFastMode(string modelId)
    {
        return ModelConfigLoader.SupportsFastMode(ProviderConfigKey, modelId);
    }

    public virtual bool SupportsEffort(string modelId)
    {
        return ModelConfigLoader.SupportsEffort(ProviderConfigKey, modelId);
    }

    public virtual bool SupportsMaxEffort(string modelId)
    {
        return ModelConfigLoader.SupportsMaxEffort(ProviderConfigKey, modelId);
    }

    public virtual string? DefaultApiVersion => null;
    public virtual string? ResolveEndpointFromEnv() => null;
    public virtual bool IsCompoundAuthFormat(string apiKey) => false;
    public virtual string? ExtractApiKeyFromCompound(string apiKey) => null;
    public virtual bool SupportsOAuth => false;
    public virtual OAuthConfig? GetOAuthConfig() => null;
    public virtual bool SupportsWebSearch => false;
    public virtual bool RequiresInteractiveEndpoint => false;
    public virtual string SerializeAuthCredentials(string apiKey, string? endpoint) => apiKey;
    public virtual string? EndpointPromptText => null;
    public virtual string? EndpointRequiredMessage => null;
}
