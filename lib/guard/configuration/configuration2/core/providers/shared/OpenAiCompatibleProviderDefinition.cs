
namespace Core.Configuration.Providers;

/// <summary>
/// OpenAI 兼容协议供应商 — 通用实现，所有配置从 ProfileSettings 读取
/// 覆盖 openai/deepseek/agnes/sensenova 等所有 OpenAI 兼容供应商
/// </summary>
public sealed class OpenAiCompatibleProviderDefinition : IProviderDefinition {
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly string _providerName;
    private readonly string? _apiKeyEnvVar;

    /// <summary>
    /// 构造 OpenAI 兼容供应商定义
    /// </summary>
    public OpenAiCompatibleProviderDefinition(IModelConfigLoader modelConfigLoader, string providerName, string? apiKeyEnvVar = null) {
        _modelConfigLoader = modelConfigLoader;
        _providerName = providerName;
        _apiKeyEnvVar = apiKeyEnvVar;
    }

    /// <inheritdoc />
    public VendorKind Vendor => VendorKindExtensions.FromValue(_providerName) ?? VendorKind.OpenAi;

    /// <inheritdoc />
    public ProtocolKind Protocol => ProtocolKind.OpenAiCompatible;

    /// <inheritdoc />
    public string ProviderName => _providerName;

    /// <inheritdoc />
    public string DisplayName => _providerName;

    /// <inheritdoc />
    public string DefaultModelId => _modelConfigLoader.GetDefaultModelId(_providerName);

    /// <inheritdoc />
    public string DefaultFastModelId => _modelConfigLoader.GetDefaultFastModelId(_providerName);

    /// <inheritdoc />
    public string? DefaultEndpoint => null;

    /// <inheritdoc />
    public string? ApiKeyEnvironmentVariable => _apiKeyEnvVar;

    /// <inheritdoc />
    public string? EndpointEnvironmentVariable => null;

    /// <inheritdoc />
    public string GetBaseUrl(ProviderConfig config)
        => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.openai.com/v1/";

    /// <inheritdoc />
    public string GetChatEndpoint(ProviderConfig config) {
        if (config.ProtocolKind == ProtocolKind.OpenAiResponses)
            return "responses";
        if (!string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return "chat/completions";
    }

    /// <inheritdoc />
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config) {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
    }

    /// <inheritdoc />
    public string? ResolveApiKeyFromEnv() {
        if (_apiKeyEnvVar is not null) {
            var key = Environment.GetEnvironmentVariable(_apiKeyEnvVar);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        return Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
    }

    /// <inheritdoc />
    public bool IsValid(ProviderConfig config)
        => !string.IsNullOrWhiteSpace(config.ApiKey) || config.EnableOAuthTokenSupport;

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