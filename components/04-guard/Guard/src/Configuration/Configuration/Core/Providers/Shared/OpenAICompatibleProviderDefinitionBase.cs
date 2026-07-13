
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

    public abstract string GetBaseUrl(ProviderConfig config);
    public abstract string GetChatEndpoint(ProviderConfig config);
    public abstract void ConfigureHttpClient(HttpClient client, ProviderConfig config);
    public abstract string? ResolveApiKeyFromEnv();
    public abstract bool IsValid(ProviderConfig config);

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
