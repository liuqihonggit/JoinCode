
namespace Core.Configuration.Providers;

/// <summary>
/// Azure OpenAI 供应商 — OAuth + 复合认证 + 特殊 URL 格式
/// </summary>
public sealed class AzureProviderDefinition : IProviderDefinition
{
    private readonly IModelConfigLoader _modelConfigLoader;

    public AzureProviderDefinition(IModelConfigLoader modelConfigLoader)
    {
        _modelConfigLoader = modelConfigLoader;
    }

    public VendorKind Vendor => VendorKind.Azure;
    public ProtocolKind Protocol => ProtocolKind.Azure;
    public string ProviderName => VendorKind.Azure.ToValue();
    public string DisplayName => "Azure OpenAI";
    public string DefaultModelId => _modelConfigLoader.GetDefaultModelId(VendorKindConstants.OpenAi);
    public string DefaultFastModelId => _modelConfigLoader.GetDefaultFastModelId(VendorKindConstants.OpenAi);
    public string? DefaultEndpoint => null;
    public string? ApiKeyEnvironmentVariable => ProviderEnvVar.AzureOpenAiApiKey.ToValue();
    public string? EndpointEnvironmentVariable => ProviderEnvVar.AzureOpenAiEndpoint.ToValue();
    public string? DefaultApiVersion => "2024-02-01";

    public string GetBaseUrl(ProviderConfig config)
        => $"{config.Endpoint?.TrimEnd('/')}/openai/deployments/{config.ModelId}";

    public string GetChatEndpoint(ProviderConfig config)
        => $"chat/completions?api-version={config.ApiVersion}";

    public void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
    }

    public string? ResolveApiKeyFromEnv()
        => Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiApiKey.ToValue());

    public string? ResolveEndpointFromEnv()
        => Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiEndpoint.ToValue());

    public bool IsValid(ProviderConfig config)
        => !string.IsNullOrWhiteSpace(config.ApiKey) && !string.IsNullOrWhiteSpace(config.Endpoint);

    public bool IsCompoundAuthFormat(string apiKey) => apiKey.StartsWith("{");
    public bool SupportsOAuth => true;
    public OAuthConfig? GetOAuthConfig() => new()
    {
        Provider = VendorKindConstants.Azure,
        ClientId = Environment.GetEnvironmentVariable(JccEnvVar.AzureClientId.ToValue()) ?? "",
        AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        RedirectUri = "http://localhost:5000/oauth/callback",
        Scope = new List<string> { "https://cognitiveservices.azure.com/.default" }
    };

    public string? ExtractApiKeyFromCompound(string apiKey)
    {
        try
        {
            var data = RelaxedJsonSerializer.Deserialize(apiKey, ConfigJsonContext.Default.DictionaryStringString);
            return data?.GetValueOrDefault("apiKey");
        }
        catch { return null; }
    }

    public bool RequiresInteractiveEndpoint => true;
    public string? EndpointPromptText => "请输入 Azure OpenAI Endpoint（如 https://your-resource.openai.azure.com）";
    public string? EndpointRequiredMessage => "Azure OpenAI 必须提供 Endpoint，配置已取消。";

    public string SerializeAuthCredentials(string apiKey, string? endpoint)
    {
        var authData = new Dictionary<string, string>
        {
            ["endpoint"] = endpoint ?? string.Empty,
            ["apiKey"] = apiKey
        };
        return RelaxedJsonSerializer.SerializeCompact(authData, ConfigJsonContext.Default);
    }

    public IEnumerable<ModelEntry> AvailableModels => _modelConfigLoader.GetModels(VendorKindConstants.OpenAi);
    public string? ResolveAlias(string input) => _modelConfigLoader.ResolveAlias(VendorKindConstants.OpenAi, input);
    public bool SupportsFastMode(string modelId) => _modelConfigLoader.SupportsFastMode(VendorKindConstants.OpenAi, modelId);
    public bool SupportsEffort(string modelId) => _modelConfigLoader.SupportsEffort(VendorKindConstants.OpenAi, modelId);
    public bool SupportsMaxEffort(string modelId) => _modelConfigLoader.SupportsMaxEffort(VendorKindConstants.OpenAi, modelId);
    public bool SupportsThinkingMode(string modelId) => _modelConfigLoader.SupportsThinkingMode(VendorKindConstants.OpenAi, modelId);
    public bool SupportsModality(string modelId, ModelModalityKind modality) => _modelConfigLoader.SupportsModality(VendorKindConstants.OpenAi, modelId, modality);
    public ModelModalityKind GetModalities(string modelId) => _modelConfigLoader.GetModalities(VendorKindConstants.OpenAi, modelId);
}
