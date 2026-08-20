
namespace Core.Configuration.Providers;

/// <summary>
/// Anthropic 兼容协议供应商 — 通用实现,任何供应商(含 DeepSeek)配 protocol:"anthropic" 即用此类
/// 对齐 OpenAiCompatibleProviderDefinition 的通用化模式:供应商身份从 providerName 推导,不硬编码
/// 协议固有:x-api-key 认证 + v1/messages 端点 + anthropic-version 头
/// 配置大于代码:endpoint 从 settings.json 读取,非 Anthropic 供应商未配端点时抛异常避免静默错发
/// </summary>
public sealed class AnthropicCompatibleProviderDefinition : IProviderDefinition
{
    private readonly IModelConfigLoader _modelConfigLoader;
    private readonly string _providerName;
    private readonly string? _apiKeyEnvVar;

    public AnthropicCompatibleProviderDefinition(IModelConfigLoader modelConfigLoader, string providerName, string? apiKeyEnvVar = null)
    {
        _modelConfigLoader = modelConfigLoader;
        _providerName = providerName;
        _apiKeyEnvVar = apiKeyEnvVar;
    }

    public VendorKind Vendor => VendorKindExtensions.FromValue(_providerName) ?? VendorKind.Anthropic;
    public ProtocolKind Protocol => ProtocolKind.Anthropic;
    public string ProviderName => _providerName;
    public string DisplayName => _providerName;
    public string DefaultModelId => _modelConfigLoader.GetDefaultModelId(_providerName);
    public string DefaultFastModelId => _modelConfigLoader.GetDefaultFastModelId(_providerName);
    public string? DefaultEndpoint => null;
    public string? ApiKeyEnvironmentVariable => _apiKeyEnvVar ?? ProviderEnvVar.AnthropicApiKey.ToValue();
    public string? EndpointEnvironmentVariable => null;

    /// <summary>
    /// 获取基础 URL — 优先从配置读取,Anthropic 供应商回退官方,其他供应商未配置时抛异常避免静默错发
    /// </summary>
    public string GetBaseUrl(ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.Endpoint))
            return config.Endpoint.TrimEnd('/') + "/";
        if (string.Equals(_providerName, "anthropic", StringComparison.OrdinalIgnoreCase))
            return "https://api.anthropic.com/";
        throw new InvalidOperationException(
            $"供应商 '{_providerName}' 使用 Anthropic 协议但未配置 endpoint。" +
            $"请在 settings.json 的 vendor.{_providerName}.endpoint 配置 Anthropic 兼容端点" +
            $"(如 DeepSeek 为 https://api.deepseek.com/anthropic)。");
    }

    public string GetChatEndpoint(ProviderConfig config) => "v1/messages";

    /// <summary>
    /// 配置 HttpClient — Anthropic 协议固有认证头(x-api-key + anthropic-version)
    /// 注意:不发 anthropic-beta 头,因为 DeepSeek 等 Anthropic 兼容端点不一定支持 Anthropic 的 beta 特性
    /// </summary>
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            client.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2024-10-22");
        }
    }

    /// <summary>
    /// 从环境变量解析 API Key — 显式指定 envVar 时不回退,未指定时回退到 ANTHROPIC_API_KEY(兼容 Anthropic 默认)
    /// </summary>
    public string? ResolveApiKeyFromEnv()
    {
        if (_apiKeyEnvVar is not null)
            return Environment.GetEnvironmentVariable(_apiKeyEnvVar);
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
    public bool SupportsModality(string modelId, ModelModalityKind modality) => _modelConfigLoader.SupportsModality(_providerName, modelId, modality);
    public ModelModalityKind GetModalities(string modelId) => _modelConfigLoader.GetModalities(_providerName, modelId);
}
