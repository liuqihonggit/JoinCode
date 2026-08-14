
namespace Core.Configuration.Providers;

/// <summary>
/// Anthropic 协议供应商 — x-api-key 认证 + v1/messages 端点
/// </summary>
public sealed class AnthropicProviderDefinition : IProviderDefinition
{
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly string _providerName;
    private readonly string? _apiKeyEnvVar;

    public AnthropicProviderDefinition(IModelConfigLoader modelConfigLoader, string providerName = "anthropic", string? apiKeyEnvVar = null)
    {
        _modelConfigLoader = modelConfigLoader;
        _providerName = providerName;
        _apiKeyEnvVar = apiKeyEnvVar;
    }

    public VendorKind Vendor => VendorKind.Anthropic;
    public ProtocolKind Protocol => ProtocolKind.Anthropic;
    public string ProviderName => _providerName;
    public string DisplayName => "Anthropic";
    public string DefaultModelId => _modelConfigLoader.GetDefaultModelId(_providerName);
    public string DefaultFastModelId => _modelConfigLoader.GetDefaultFastModelId(_providerName);
    public string? DefaultEndpoint => null;
    public string? ApiKeyEnvironmentVariable => _apiKeyEnvVar ?? ProviderEnvVar.AnthropicApiKey.ToValue();
    public string? EndpointEnvironmentVariable => null;

    public string GetBaseUrl(ProviderConfig config)
        => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.anthropic.com/";

    public string GetChatEndpoint(ProviderConfig config) => "v1/messages";

    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
            client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31,prompt-caching-scope-2026-01-05,context-management-2025-06-27");
        }
    }

    public string? ResolveApiKeyFromEnv()
    {
        if (_apiKeyEnvVar is not null)
        {
            var key = Environment.GetEnvironmentVariable(_apiKeyEnvVar);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AnthropicApiKey.ToValue());
    }

    public bool IsValid(ProviderConfig config)
        => !string.IsNullOrWhiteSpace(config.ApiKey) || config.EnableOAuthTokenSupport;

    public bool SupportsWebSearch => true;

    public IEnumerable<ModelEntry> AvailableModels => _modelConfigLoader.GetModels(_providerName);
    public string? ResolveAlias(string input) => _modelConfigLoader.ResolveAlias(_providerName, input);
    public bool SupportsFastMode(string modelId) => _modelConfigLoader.SupportsFastMode(_providerName, modelId);
    public bool SupportsEffort(string modelId) => _modelConfigLoader.SupportsEffort(_providerName, modelId);
    public bool SupportsMaxEffort(string modelId) => _modelConfigLoader.SupportsMaxEffort(_providerName, modelId);
    public bool SupportsThinkingMode(string modelId) => _modelConfigLoader.SupportsThinkingMode(_providerName, modelId);
}
