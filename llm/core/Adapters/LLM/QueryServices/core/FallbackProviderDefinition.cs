
namespace Api.LLM.QueryServices;

internal sealed class FallbackProviderDefinition : IProviderDefinition {
    private readonly IProviderDefinition? _inner;
    private readonly ProtocolKind _protocol;
    private readonly IModelConfigLoader? _modelConfigLoader;

    /// <summary>构造回退供应商定义。</summary>
    /// <param name="protocol">协议类型。</param>
    /// <param name="modelConfigLoader">模型配置加载器。</param>
    public FallbackProviderDefinition(ProtocolKind protocol, IModelConfigLoader? modelConfigLoader = null) {
        _protocol = protocol;
        _modelConfigLoader = modelConfigLoader;
    }

    /// <summary>构造回退供应商定义。</summary>
    /// <param name="inner">内部供应商定义。</param>
    public FallbackProviderDefinition(IProviderDefinition inner) {
        _inner = inner;
        _protocol = inner.Protocol;
    }

    /// <summary>获取供应商类型。</summary>
    public VendorKind Vendor => _inner?.Vendor ?? VendorKind.DeepSeek;
    /// <summary>获取协议类型。</summary>
    public ProtocolKind Protocol => _inner?.Protocol ?? _protocol;
    /// <summary>获取供应商名称。</summary>
    public string ProviderName => _inner?.ProviderName ?? _protocol.ToValue();
    /// <summary>获取显示名称。</summary>
    public string DisplayName => _inner?.DisplayName ?? _protocol.ToValue();
    /// <summary>获取默认模型标识。</summary>
    public string DefaultModelId => _inner?.DefaultModelId ?? _modelConfigLoader?.GetDefaultModelId(ProtocolToConfigKey()) ?? string.Empty;
    /// <summary>获取默认快速模型标识。</summary>
    public string DefaultFastModelId => _inner?.DefaultFastModelId ?? _modelConfigLoader?.GetDefaultFastModelId(ProtocolToConfigKey()) ?? string.Empty;
    /// <summary>获取默认端点。</summary>
    public string? DefaultEndpoint => _inner?.DefaultEndpoint;
    /// <summary>获取 API Key 环境变量名。</summary>
    public string? ApiKeyEnvironmentVariable => _inner?.ApiKeyEnvironmentVariable;
    /// <summary>获取端点环境变量名。</summary>
    public string? EndpointEnvironmentVariable => _inner?.EndpointEnvironmentVariable;
    /// <summary>获取可用模型列表。</summary>
    public IEnumerable<ModelEntry> AvailableModels => _inner?.AvailableModels ?? [];
    /// <summary>从环境变量解析 API Key。</summary>
    public string? ResolveApiKeyFromEnv() => _inner?.ResolveApiKeyFromEnv();
    /// <summary>验证配置是否有效。</summary>
    /// <param name="config">供应商配置。</param>
    public bool IsValid(ProviderConfig config) => _inner?.IsValid(config) ?? !string.IsNullOrWhiteSpace(config.ApiKey);

    /// <summary>获取基础 URL。</summary>
    /// <param name="config">供应商配置。</param>
    public string GetBaseUrl(ProviderConfig config) => _inner?.GetBaseUrl(config) ?? _protocol switch {
        ProtocolKind.Anthropic => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.anthropic.com/",
        ProtocolKind.Azure => $"{config.Endpoint?.TrimEnd('/')}/openai/deployments/{config.ModelId}",
        _ => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.openai.com/v1/"
    };

    /// <summary>获取聊天端点。</summary>
    /// <param name="config">供应商配置。</param>
    public string GetChatEndpoint(ProviderConfig config) => _inner?.GetChatEndpoint(config) ?? _protocol switch {
        ProtocolKind.Anthropic => "v1/messages",
        ProtocolKind.Azure => $"chat/completions?api-version={config.ApiVersion}",
        ProtocolKind.OpenAiResponses => "responses",
        _ => !string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : "chat/completions"
    };

    /// <summary>配置 HTTP 客户端。</summary>
    /// <param name="client">HTTP 客户端。</param>
    /// <param name="config">供应商配置。</param>
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config) {
        if (_inner is not null) {
            _inner.ConfigureHttpClient(client, config);
            return;
        }

        if (string.IsNullOrEmpty(config.ApiKey)) return;

        switch (_protocol) {
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

    private string ProtocolToConfigKey() => _protocol switch {
        ProtocolKind.Anthropic => VendorKindEnumConstants.Anthropic,
        ProtocolKind.Agnes => VendorKindEnumConstants.Agnes,
        ProtocolKind.Azure => VendorKindEnumConstants.OpenAi,
        _ => VendorKindEnumConstants.OpenAi
    };
}