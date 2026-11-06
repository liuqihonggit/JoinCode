
namespace Core.Configuration.Providers;

/// <summary>
/// Jev (TypeSafe AI) 供应商定义 — System One Model,返回类型化概率决策而非文本
/// 协议固有:Bearer Token 认证 + systemone 端点 + state/questions 请求体
/// 配置大于代码:endpoint 从 settings.json 读取,未配置时回退官方 https://api.typesafe.ai/v1/
/// </summary>
public sealed class JevProviderDefinition : IProviderDefinition {
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly string _providerName;
    private readonly string? _apiKeyEnvVar;

    /// <summary>
    /// 构造 Jev 供应商定义
    /// </summary>
    /// <param name="modelConfigLoader">模型配置加载器,从 models.json 读取模型列表/别名/能力</param>
    /// <param name="providerName">供应商标识(默认 "jev")</param>
    /// <param name="apiKeyEnvVar">API Key 环境变量名(默认 JEV_API_KEY)</param>
    public JevProviderDefinition(IModelConfigLoader modelConfigLoader, string providerName = VendorKindEnumConstants.Jev, string? apiKeyEnvVar = null) {
        _modelConfigLoader = modelConfigLoader;
        _providerName = providerName;
        _apiKeyEnvVar = apiKeyEnvVar;
    }

    /// <inheritdoc />
    public VendorKind Vendor => VendorKind.Jev;
    /// <inheritdoc />
    public ProtocolKind Protocol => ProtocolKind.Jev;
    /// <inheritdoc />
    public string ProviderName => _providerName;
    /// <inheritdoc />
    public string DisplayName => "Jev (TypeSafe AI)";
    /// <inheritdoc />
    public string DefaultModelId => _modelConfigLoader.GetDefaultModelId(_providerName);
    /// <inheritdoc />
    public string DefaultFastModelId => _modelConfigLoader.GetDefaultFastModelId(_providerName);
    /// <inheritdoc />
    public string? DefaultEndpoint => "https://api.typesafe.ai/v1/systemone";
    /// <inheritdoc />
    public string? ApiKeyEnvironmentVariable => _apiKeyEnvVar ?? ProviderEnvVar.JevApiKey.ToValue();
    /// <inheritdoc />
    public string? EndpointEnvironmentVariable => null;

    /// <summary>
    /// 获取基础 URL — 优先从配置读取,未配置时回退官方 https://api.typesafe.ai/v1/
    /// </summary>
    public string GetBaseUrl(ProviderConfig config) {
        if (!string.IsNullOrEmpty(config.Endpoint)) {
            var endpoint = config.Endpoint.TrimEnd('/');
            return endpoint + "/";
        }
        return "https://api.typesafe.ai/v1/";
    }

    /// <inheritdoc />
    public string GetChatEndpoint(ProviderConfig config) => "systemone";

    /// <summary>
    /// 配置 HttpClient — Jev 协议固有认证头(Authorization: Bearer)
    /// </summary>
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config) {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
    }

    /// <summary>
    /// 从环境变量解析 API Key — 显式指定 envVar 时不回退,未指定时回退到 JEV_API_KEY
    /// </summary>
    public string? ResolveApiKeyFromEnv() {
        if (_apiKeyEnvVar is not null)
            return Environment.GetEnvironmentVariable(_apiKeyEnvVar);
        return Environment.GetEnvironmentVariable(ProviderEnvVar.JevApiKey.ToValue());
    }

    /// <inheritdoc />
    public bool IsValid(ProviderConfig config) => !string.IsNullOrWhiteSpace(config.ApiKey);

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
    public bool SupportsModality(string modelId, ModelModalityKind modality) => _modelConfigLoader.SupportsModality(_providerName, modelId, modality);
    /// <inheritdoc />
    public ModelModalityKind GetModalities(string modelId) => _modelConfigLoader.GetModalities(_providerName, modelId);
}
