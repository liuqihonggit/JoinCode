
namespace Core.Configuration.Providers;

/// <summary>
/// Anthropic 协议供应商 — x-api-key 认证 + v1/messages 端点
/// </summary>
public sealed class AnthropicProviderDefinition : IProviderDefinition {
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly string _providerName;
    private readonly string? _apiKeyEnvVar;

    /// <summary>
    /// 构造 Anthropic 协议供应商定义
    /// </summary>
    public AnthropicProviderDefinition(IModelConfigLoader modelConfigLoader, string providerName = "anthropic", string? apiKeyEnvVar = null) {
        _modelConfigLoader = modelConfigLoader;
        _providerName = providerName;
        _apiKeyEnvVar = apiKeyEnvVar;
    }

    /// <inheritdoc />
    public VendorKind Vendor => VendorKind.Anthropic;
    /// <inheritdoc />
    public ProtocolKind Protocol => ProtocolKind.Anthropic;
    /// <inheritdoc />
    public string ProviderName => _providerName;
    /// <inheritdoc />
    public string DisplayName => "Anthropic";
    /// <inheritdoc />
    public string DefaultModelId => _modelConfigLoader.GetDefaultModelId(_providerName);
    /// <inheritdoc />
    public string DefaultFastModelId => _modelConfigLoader.GetDefaultFastModelId(_providerName);
    /// <inheritdoc />
    public string? DefaultEndpoint => null;
    /// <inheritdoc />
    public string? ApiKeyEnvironmentVariable => _apiKeyEnvVar ?? ProviderEnvVar.AnthropicApiKey.ToValue();
    /// <inheritdoc />
    public string? EndpointEnvironmentVariable => null;

    /// <inheritdoc />
    public string GetBaseUrl(ProviderConfig config)
        => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.anthropic.com/";

    /// <inheritdoc />
    public string GetChatEndpoint(ProviderConfig config) => "v1/messages";

    /// <inheritdoc />
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config) {
        if (!string.IsNullOrEmpty(config.ApiKey)) {
            client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
            client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31,prompt-caching-scope-2026-01-05,context-management-2025-06-27");
        }
    }

    /// <inheritdoc />
    public string? ResolveApiKeyFromEnv() {
        if (_apiKeyEnvVar is not null) {
            var key = Environment.GetEnvironmentVariable(_apiKeyEnvVar);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AnthropicApiKey.ToValue());
    }

    /// <inheritdoc />
    public bool IsValid(ProviderConfig config)
        => !string.IsNullOrWhiteSpace(config.ApiKey) || config.EnableOAuthTokenSupport;

    /// <inheritdoc />
    public bool SupportsWebSearch => true;

    /// <inheritdoc />
    public IEnumerable<ModelEntry> AvailableModels => _modelConfigLoader.GetModels(_providerName);
    /// <inheritdoc />
    public string? ResolveAlias(string input) => _modelConfigLoader.ResolveAlias(_providerName, input);
    /// <inheritdoc />
    public bool SupportsFastMode(string modelId) => _modelConfigLoader.SupportsFastMode(_providerName, modelId);
    /// <inheritdoc />
    public bool SupportsEffort(string modelId) => _modelConfigLoader.SupportsEffort(_providerName, modelId);
    /// <inheritdoc />
    public bool SupportsMaxEffort(string modelId) => _modelConfigLoader.SupportsMaxEffort(_providerName, modelId);
    /// <inheritdoc />
    public bool SupportsThinkingMode(string modelId) => _modelConfigLoader.SupportsThinkingMode(_providerName, modelId);
    /// <inheritdoc />
    public bool SupportsModality(string modelId, ModelModalityKind modality) => _modelConfigLoader.SupportsModality(_providerName, modelId, modality);
    /// <inheritdoc />
    public ModelModalityKind GetModalities(string modelId) => _modelConfigLoader.GetModalities(_providerName, modelId);
}