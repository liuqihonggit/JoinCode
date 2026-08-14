
namespace Core.Configuration.Providers;

/// <summary>
/// OpenAI 兼容协议供应商 — 通用实现，所有配置从 ProfileSettings 读取
/// 覆盖 openai/deepseek/agnes/sensenova 等所有 OpenAI 兼容供应商
/// </summary>
public sealed class OpenAiCompatibleProviderDefinition : IProviderDefinition
{
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly string _providerName;
    private readonly string? _apiKeyEnvVar;

    public OpenAiCompatibleProviderDefinition(IModelConfigLoader modelConfigLoader, string providerName, string? apiKeyEnvVar = null)
    {
        _modelConfigLoader = modelConfigLoader;
        _providerName = providerName;
        _apiKeyEnvVar = apiKeyEnvVar;
    }

    public VendorKind Vendor => VendorKindExtensions.FromValue(_providerName) ?? VendorKind.OpenAi;
    public ProtocolKind Protocol => ProtocolKind.OpenAiCompatible;
    public string ProviderName => _providerName;
    public string DisplayName => _providerName;
    public string DefaultModelId => _modelConfigLoader.GetDefaultModelId(_providerName);
    public string DefaultFastModelId => _modelConfigLoader.GetDefaultFastModelId(_providerName);
    public string? DefaultEndpoint => null;
    public string? ApiKeyEnvironmentVariable => _apiKeyEnvVar;
    public string? EndpointEnvironmentVariable => null;

    public string GetBaseUrl(ProviderConfig config)
        => !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : "https://api.openai.com/v1/";

    public string GetChatEndpoint(ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return "chat/completions";
    }

    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
    }

    public string? ResolveApiKeyFromEnv()
    {
        if (_apiKeyEnvVar is not null)
        {
            var key = Environment.GetEnvironmentVariable(_apiKeyEnvVar);
            if (!string.IsNullOrEmpty(key)) return key;
        }
        return Environment.GetEnvironmentVariable(ProviderEnvVar.OpenAiApiKey.ToValue());
    }

    public bool IsValid(ProviderConfig config)
        => !string.IsNullOrWhiteSpace(config.ApiKey) || config.EnableOAuthTokenSupport;

    public IEnumerable<ModelEntry> AvailableModels => _modelConfigLoader.GetModels(_providerName);
    public string? ResolveAlias(string input) => _modelConfigLoader.ResolveAlias(_providerName, input);
    public bool SupportsFastMode(string modelId) => _modelConfigLoader.SupportsFastMode(_providerName, modelId);
    public bool SupportsEffort(string modelId) => _modelConfigLoader.SupportsEffort(_providerName, modelId);
    public bool SupportsMaxEffort(string modelId) => _modelConfigLoader.SupportsMaxEffort(_providerName, modelId);
    public bool SupportsThinkingMode(string modelId) => _modelConfigLoader.SupportsThinkingMode(_providerName, modelId);
}
