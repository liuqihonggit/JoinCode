
namespace Api.LLM.QueryServices;

internal sealed class FallbackProviderDefinition : IProviderDefinition
{
    private readonly IProviderDefinition? _inner;
    private readonly ProviderKind _kind;

    public FallbackProviderDefinition(ProviderKind kind)
    {
        _kind = kind;
    }

    public FallbackProviderDefinition(IProviderDefinition inner)
    {
        _inner = inner;
        _kind = inner.Kind;
    }

    public ProviderKind Kind => _inner?.Kind ?? _kind;
    public string ProviderName => _inner?.ProviderName ?? _kind.ToValue();
    public string DisplayName => _inner?.DisplayName ?? _kind.ToValue();
    public string DefaultModelId => _inner?.DefaultModelId ?? ModelConfigLoader.GetDefaultModelId(KindToConfigKey());
    public string DefaultFastModelId => _inner?.DefaultFastModelId ?? ModelConfigLoader.GetDefaultFastModelId(KindToConfigKey());
    public string? DefaultEndpoint => _inner?.DefaultEndpoint;
    public string? ApiKeyEnvironmentVariable => _inner?.ApiKeyEnvironmentVariable;
    public string? EndpointEnvironmentVariable => _inner?.EndpointEnvironmentVariable;
    public IReadOnlyList<ModelEntry> AvailableModels => _inner?.AvailableModels ?? [];
    public string? ResolveApiKeyFromEnv() => _inner?.ResolveApiKeyFromEnv();
    public bool IsValid(ProviderConfig config) => _inner?.IsValid(config) ?? !string.IsNullOrWhiteSpace(config.ApiKey);

    public string GetBaseUrl(ProviderConfig config) => _inner?.GetBaseUrl(config) ?? _kind switch
    {
        ProviderKind.Anthropic => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.anthropic.com/",
        ProviderKind.Azure => $"{config.Endpoint?.TrimEnd('/')}/openai/deployments/{config.ModelId}",
        _ => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.openai.com/v1/"
    };

    public string GetChatEndpoint(ProviderConfig config) => _inner?.GetChatEndpoint(config) ?? _kind switch
    {
        ProviderKind.Anthropic => "v1/messages",
        ProviderKind.Azure => $"chat/completions?api-version={config.ApiVersion}",
        _ => !string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : "chat/completions"
    };

    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (_inner is not null)
        {
            _inner.ConfigureHttpClient(client, config);
            return;
        }

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

    private string KindToConfigKey() => _kind switch
    {
        ProviderKind.Anthropic => "anthropic",
        ProviderKind.DeepSeek => "deepseek",
        ProviderKind.Agnes => "agnes",
        ProviderKind.Azure => "openai",
        _ => "openai"
    };
}
