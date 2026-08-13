
namespace Api.LLM.QueryServices;

internal sealed class FallbackProviderDefinition : IProviderDefinition
{
    private readonly IProviderDefinition? _inner;
    private readonly ProtocolKind _protocol;

    public FallbackProviderDefinition(ProtocolKind protocol)
    {
        _protocol = protocol;
    }

    public FallbackProviderDefinition(IProviderDefinition inner)
    {
        _inner = inner;
        _protocol = inner.Protocol;
    }

    public VendorKind Vendor => _inner?.Vendor ?? VendorKind.DeepSeek;
    public ProtocolKind Protocol => _inner?.Protocol ?? _protocol;
    public string ProviderName => _inner?.ProviderName ?? _protocol.ToValue();
    public string DisplayName => _inner?.DisplayName ?? _protocol.ToValue();
    public string DefaultModelId => _inner?.DefaultModelId ?? ModelConfigLoader.GetDefaultModelId(ProtocolToConfigKey());
    public string DefaultFastModelId => _inner?.DefaultFastModelId ?? ModelConfigLoader.GetDefaultFastModelId(ProtocolToConfigKey());
    public string? DefaultEndpoint => _inner?.DefaultEndpoint;
    public string? ApiKeyEnvironmentVariable => _inner?.ApiKeyEnvironmentVariable;
    public string? EndpointEnvironmentVariable => _inner?.EndpointEnvironmentVariable;
    public IEnumerable<ModelEntry> AvailableModels => _inner?.AvailableModels ?? [];
    public string? ResolveApiKeyFromEnv() => _inner?.ResolveApiKeyFromEnv();
    public bool IsValid(ProviderConfig config) => _inner?.IsValid(config) ?? !string.IsNullOrWhiteSpace(config.ApiKey);

    public string GetBaseUrl(ProviderConfig config) => _inner?.GetBaseUrl(config) ?? _protocol switch
    {
        ProtocolKind.Anthropic => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.anthropic.com/",
        ProtocolKind.Azure => $"{config.Endpoint?.TrimEnd('/')}/openai/deployments/{config.ModelId}",
        _ => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.openai.com/v1/"
    };

    public string GetChatEndpoint(ProviderConfig config) => _inner?.GetChatEndpoint(config) ?? _protocol switch
    {
        ProtocolKind.Anthropic => "v1/messages",
        ProtocolKind.Azure => $"chat/completions?api-version={config.ApiVersion}",
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

        switch (_protocol)
        {
            case ProtocolKind.Anthropic:
                client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
                break;
            case ProtocolKind.Azure:
                client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
                break;
            default:
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
                break;
        }
    }

    private string ProtocolToConfigKey() => _protocol switch
    {
        ProtocolKind.Anthropic => "anthropic",
        ProtocolKind.Agnes => "agnes",
        ProtocolKind.Azure => "openai",
        _ => "openai"
    };
}
