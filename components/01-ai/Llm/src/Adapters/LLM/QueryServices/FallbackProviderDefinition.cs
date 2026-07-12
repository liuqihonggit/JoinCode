
namespace Api.LLM.QueryServices;

internal sealed class FallbackProviderDefinition : IProviderDefinition
{
    private readonly ProviderKind _kind;

    public FallbackProviderDefinition(ProviderKind kind)
    {
        _kind = kind;
    }

    public ProviderKind Kind => _kind;
    public string ProviderName => _kind.ToValue();
    public string DisplayName => _kind.ToValue();
    public string DefaultModelId => _kind switch
    {
        ProviderKind.Anthropic => ModelConfigLoader.GetDefaultModelId("anthropic"),
        ProviderKind.DeepSeek => ModelConfigLoader.GetDefaultModelId("deepseek"),
        ProviderKind.Agnes => ModelConfigLoader.GetDefaultModelId("agnes"),
        _ => ModelConfigLoader.GetDefaultModelId("openai")
    };
    public string DefaultFastModelId => _kind switch
    {
        ProviderKind.Anthropic => ModelConfigLoader.GetDefaultFastModelId("anthropic"),
        ProviderKind.DeepSeek => ModelConfigLoader.GetDefaultFastModelId("deepseek"),
        ProviderKind.Agnes => ModelConfigLoader.GetDefaultFastModelId("agnes"),
        _ => ModelConfigLoader.GetDefaultFastModelId("openai")
    };
    public string? DefaultEndpoint => null;
    public string? ApiKeyEnvironmentVariable => null;
    public string? EndpointEnvironmentVariable => null;
    public IReadOnlyList<ModelEntry> AvailableModels => [];

    public string? ResolveApiKeyFromEnv() => null;
    public bool IsValid(ProviderConfig config) => !string.IsNullOrWhiteSpace(config.ApiKey);

    public string GetBaseUrl(ProviderConfig config) => _kind switch
    {
        ProviderKind.Anthropic => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.anthropic.com/",
        ProviderKind.Azure => $"{config.Endpoint?.TrimEnd('/')}/openai/deployments/{config.ModelId}",
        _ => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.openai.com/v1/"
    };

    public string GetChatEndpoint(ProviderConfig config) => _kind switch
    {
        ProviderKind.Anthropic => "v1/messages",
        ProviderKind.Azure => $"chat/completions?api-version={config.ApiVersion}",
        _ => !string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : "chat/completions"
    };

    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey)) return;

        switch (_kind)
        {
            case ProviderKind.Anthropic:
                client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
                break;
            case ProviderKind.Azure:
                client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
                break;
            default:
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
                break;
        }
    }
}
