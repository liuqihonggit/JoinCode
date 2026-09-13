
namespace Core.Configuration.Providers;

/// <summary>
/// OpenAI 兼容协议供应商定义基类 — 为 OpenAI 兼容供应商提供通用实现骨架
/// </summary>
public abstract class OpenAICompatibleProviderDefinitionBase : IProviderDefinition
{
    /// <summary>模型配置加载器，用于读取供应商配置</summary>
    protected readonly IModelConfigLoader _modelConfigLoader;

    /// <summary>构造函数 — 注入模型配置加载器</summary>
    /// <param name="modelConfigLoader">模型配置加载器</param>
    protected OpenAICompatibleProviderDefinitionBase(IModelConfigLoader modelConfigLoader)
    {
        _modelConfigLoader = modelConfigLoader;
    }

    /// <summary>供应商配置键名，默认为 "openai"</summary>
    protected virtual string ProviderConfigKey => "openai";

    /// <inheritdoc />
    public abstract VendorKind Vendor { get; }

    /// <inheritdoc />
    public virtual ProtocolKind Protocol => ProtocolKind.OpenAiCompatible;

    /// <inheritdoc />
    public abstract string ProviderName { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract string DefaultModelId { get; }

    /// <inheritdoc />
    public abstract string DefaultFastModelId { get; }

    /// <inheritdoc />
    public abstract string? DefaultEndpoint { get; }

    /// <inheritdoc />
    public abstract string? ApiKeyEnvironmentVariable { get; }

    /// <inheritdoc />
    public abstract string? EndpointEnvironmentVariable { get; }

    /// <summary>默认基础 URL，默认为 OpenAI 官方端点</summary>
    protected virtual string DefaultBaseUrl => "https://api.openai.com/v1/";
    /// <summary>聊天补全路径，默认为 "chat/completions"</summary>
    protected virtual string ChatCompletionsPath => "chat/completions";
    /// <summary>认证头名称，默认为 "Authorization"</summary>
    protected virtual string AuthHeaderName => "Authorization";
    /// <summary>认证头值前缀，默认为 "Bearer "</summary>
    protected virtual string AuthHeaderValuePrefix => "Bearer ";

    /// <inheritdoc />
    public virtual string GetBaseUrl(ProviderConfig config)
    {
        return !string.IsNullOrEmpty(config.Endpoint) ? config.Endpoint.TrimEnd('/') + "/" : DefaultBaseUrl;
    }

    /// <inheritdoc />
    public virtual string GetChatEndpoint(ProviderConfig config)
    {
        if (config.ProtocolKind == ProtocolKind.OpenAiResponses)
            return "responses";
        if (!string.IsNullOrEmpty(config.Endpoint) && config.Endpoint.TrimEnd('/').EndsWith(ChatCompletionsPath, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return ChatCompletionsPath;
    }

    /// <inheritdoc />
    public virtual void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add(AuthHeaderName, $"{AuthHeaderValuePrefix}{config.ApiKey}");
    }

    /// <inheritdoc />
    public abstract string? ResolveApiKeyFromEnv();

    /// <inheritdoc />
    public virtual bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    /// <inheritdoc />
    public virtual IEnumerable<ModelEntry> AvailableModels => _modelConfigLoader.GetModels(ProviderConfigKey);

    /// <inheritdoc />
    public virtual string? ResolveAlias(string input)
    {
        return _modelConfigLoader.ResolveAlias(ProviderConfigKey, input);
    }

    /// <inheritdoc />
    public virtual bool SupportsFastMode(string modelId)
    {
        return _modelConfigLoader.SupportsFastMode(ProviderConfigKey, modelId);
    }

    /// <inheritdoc />
    public virtual bool SupportsEffort(string modelId)
    {
        return _modelConfigLoader.SupportsEffort(ProviderConfigKey, modelId);
    }

    /// <inheritdoc />
    public virtual bool SupportsMaxEffort(string modelId)
    {
        return _modelConfigLoader.SupportsMaxEffort(ProviderConfigKey, modelId);
    }

    /// <inheritdoc />
    public virtual bool SupportsModality(string modelId, ModelModalityKind modality)
    {
        return _modelConfigLoader.SupportsModality(ProviderConfigKey, modelId, modality);
    }

    /// <inheritdoc />
    public virtual ModelModalityKind GetModalities(string modelId)
    {
        return _modelConfigLoader.GetModalities(ProviderConfigKey, modelId);
    }

    /// <inheritdoc />
    public virtual string? DefaultApiVersion => null;

    /// <inheritdoc />
    public virtual string? ResolveEndpointFromEnv() => null;

    /// <inheritdoc />
    public virtual bool IsCompoundAuthFormat(string apiKey) => false;

    /// <inheritdoc />
    public virtual string? ExtractApiKeyFromCompound(string apiKey) => null;

    /// <inheritdoc />
    public virtual bool SupportsOAuth => false;

    /// <inheritdoc />
    public virtual OAuthConfig? GetOAuthConfig() => null;

    /// <inheritdoc />
    public virtual bool SupportsWebSearch => false;

    /// <inheritdoc />
    public virtual bool RequiresInteractiveEndpoint => false;

    /// <inheritdoc />
    public virtual string SerializeAuthCredentials(string apiKey, string? endpoint) => apiKey;

    /// <inheritdoc />
    public virtual string? EndpointPromptText => null;

    /// <inheritdoc />
    public virtual string? EndpointRequiredMessage => null;
}
