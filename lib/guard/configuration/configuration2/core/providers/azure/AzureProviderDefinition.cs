
namespace Core.Configuration.Providers;

/// <summary>
/// Azure OpenAI 供应商 — OAuth + 复合认证 + 特殊 URL 格式
/// </summary>
public sealed class AzureProviderDefinition : IProviderDefinition
{
    private readonly IModelConfigLoader _modelConfigLoader;

    /// <summary>
    /// 构造 Azure OpenAI 供应商定义
    /// </summary>
    public AzureProviderDefinition(IModelConfigLoader modelConfigLoader)
    {
        _modelConfigLoader = modelConfigLoader;
    }

    /// <inheritdoc />
    public VendorKind Vendor => VendorKind.Azure;
    /// <inheritdoc />
    public ProtocolKind Protocol => ProtocolKind.Azure;
    /// <inheritdoc />
    public string ProviderName => VendorKind.Azure.ToValue();
    /// <inheritdoc />
    public string DisplayName => "Azure OpenAI";
    /// <inheritdoc />
    public string DefaultModelId => _modelConfigLoader.GetDefaultModelId(VendorKindEnumConstants.OpenAi);
    /// <inheritdoc />
    public string DefaultFastModelId => _modelConfigLoader.GetDefaultFastModelId(VendorKindEnumConstants.OpenAi);
    /// <inheritdoc />
    public string? DefaultEndpoint => null;
    /// <inheritdoc />
    public string? ApiKeyEnvironmentVariable => ProviderEnvVar.AzureOpenAiApiKey.ToValue();
    /// <inheritdoc />
    public string? EndpointEnvironmentVariable => ProviderEnvVar.AzureOpenAiEndpoint.ToValue();
    /// <inheritdoc />
    public string? DefaultApiVersion => "2024-02-01";

    /// <inheritdoc />
    public string GetBaseUrl(ProviderConfig config)
        => $"{config.Endpoint?.TrimEnd('/')}/openai/deployments/{config.ModelId}";

    /// <inheritdoc />
    public string GetChatEndpoint(ProviderConfig config)
        => $"chat/completions?api-version={config.ApiVersion}";

    /// <inheritdoc />
    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
    }

    /// <inheritdoc />
    public string? ResolveApiKeyFromEnv()
        => Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiApiKey.ToValue());

    /// <inheritdoc />
    public string? ResolveEndpointFromEnv()
        => Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiEndpoint.ToValue());

    /// <inheritdoc />
    public bool IsValid(ProviderConfig config)
        => !string.IsNullOrWhiteSpace(config.ApiKey) && !string.IsNullOrWhiteSpace(config.Endpoint);

    /// <inheritdoc />
    public bool IsCompoundAuthFormat(string apiKey) => apiKey.StartsWith("{");
    /// <inheritdoc />
    public bool SupportsOAuth => true;
    /// <inheritdoc />
    public OAuthConfig? GetOAuthConfig() => new()
    {
        Provider = VendorKindEnumConstants.Azure,
        ClientId = Environment.GetEnvironmentVariable(JccEnvVar.AzureClientId.ToValue()) ?? "",
        AuthorizationEndpoint = JccEndpointsResolver.AzureOAuthAuthorizeUrl,
        TokenEndpoint = JccEndpointsResolver.AzureOAuthTokenUrl,
        RedirectUri = JccEndpoints.AzureOAuthRedirectUri,
        Scope = new List<string> { JccEndpoints.AzureOAuthScope }
    };

    /// <inheritdoc />
    public string? ExtractApiKeyFromCompound(string apiKey)
    {
        try
        {
            var data = RelaxedJsonSerializer.Deserialize(apiKey, ConfigJsonContext.Default.DictionaryStringString);
            return data?.GetValueOrDefault("apiKey");
        }
        catch { return null; }
    }

    /// <inheritdoc />
    public bool RequiresInteractiveEndpoint => true;
    /// <inheritdoc />
    public string? EndpointPromptText => "请输入 Azure OpenAI Endpoint（如 https://your-resource.openai.azure.com）";
    /// <inheritdoc />
    public string? EndpointRequiredMessage => "Azure OpenAI 必须提供 Endpoint，配置已取消。";

    /// <inheritdoc />
    public string SerializeAuthCredentials(string apiKey, string? endpoint)
    {
        var authData = new Dictionary<string, string>
        {
            ["endpoint"] = endpoint ?? string.Empty,
            ["apiKey"] = apiKey
        };
        return RelaxedJsonSerializer.SerializeCompact(authData, ConfigJsonContext.Default);
    }

    /// <inheritdoc />
    public IEnumerable<ModelEntry> AvailableModels => _modelConfigLoader.GetModels(VendorKindEnumConstants.OpenAi);
    /// <inheritdoc />
    public string? ResolveAlias(string input) => _modelConfigLoader.ResolveAlias(VendorKindEnumConstants.OpenAi, input);
    /// <inheritdoc />
    public bool SupportsFastMode(string modelId) => _modelConfigLoader.SupportsFastMode(VendorKindEnumConstants.OpenAi, modelId);
    /// <inheritdoc />
    public bool SupportsEffort(string modelId) => _modelConfigLoader.SupportsEffort(VendorKindEnumConstants.OpenAi, modelId);
    /// <inheritdoc />
    public bool SupportsMaxEffort(string modelId) => _modelConfigLoader.SupportsMaxEffort(VendorKindEnumConstants.OpenAi, modelId);
    /// <inheritdoc />
    public bool SupportsThinkingMode(string modelId) => _modelConfigLoader.SupportsThinkingMode(VendorKindEnumConstants.OpenAi, modelId);
    /// <inheritdoc />
    public bool SupportsModality(string modelId, ModelModalityKind modality) => _modelConfigLoader.SupportsModality(VendorKindEnumConstants.OpenAi, modelId, modality);
    /// <inheritdoc />
    public ModelModalityKind GetModalities(string modelId) => _modelConfigLoader.GetModalities(VendorKindEnumConstants.OpenAi, modelId);
}
