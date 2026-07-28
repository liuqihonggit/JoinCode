
namespace Core.Configuration.Providers;

public sealed class AzureProviderDefinition : OpenAICompatibleProviderDefinitionBase
{
    protected override string ProviderConfigKey => "openai";

    public override ProviderKind Kind => ProviderKind.Azure;
    public override string ProviderName => ProviderKind.Azure.ToValue();
    public override string DisplayName => "Azure OpenAI";
    public override string DefaultModelId => ModelConfigLoader.GetDefaultModelId("openai");
    public override string DefaultFastModelId => ModelConfigLoader.GetDefaultFastModelId("openai");
    public override string? DefaultEndpoint => null;
    public override string? ApiKeyEnvironmentVariable => ProviderEnvVar.AzureOpenAiApiKey.ToValue();
    public override string? EndpointEnvironmentVariable => ProviderEnvVar.AzureOpenAiEndpoint.ToValue();
    public override string? DefaultApiVersion => "2024-02-01";

    public override string GetBaseUrl(ProviderConfig config)
        => $"{config.Endpoint?.TrimEnd('/')}/openai/deployments/{config.ModelId}";

    public override string GetChatEndpoint(ProviderConfig config)
        => $"chat/completions?api-version={config.ApiVersion}";

    public override void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
    }

    public override string? ResolveApiKeyFromEnv()
        => Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiApiKey.ToValue());

    public override string? ResolveEndpointFromEnv()
        => Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiEndpoint.ToValue());

    public override bool IsValid(ProviderConfig config)
        => !string.IsNullOrWhiteSpace(config.ApiKey) && !string.IsNullOrWhiteSpace(config.Endpoint);

    public override bool IsCompoundAuthFormat(string apiKey) => apiKey.StartsWith("{");
    public override bool SupportsOAuth => true;
    public override OAuthConfig? GetOAuthConfig() => new()
    {
        Provider = "azure",
        ClientId = Environment.GetEnvironmentVariable(JccEnvVar.AzureClientId.ToValue()) ?? "",
        AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        RedirectUri = "http://localhost:5000/oauth/callback",
        Scope = new List<string> { "https://cognitiveservices.azure.com/.default" }
    };

    public override string? ExtractApiKeyFromCompound(string apiKey)
    {
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize(apiKey, ConfigJsonContext.Default.DictionaryStringString);
            return data?.GetValueOrDefault("apiKey");
        }
        catch { return null; }
    }

    public override bool RequiresInteractiveEndpoint => true;
    public override string? EndpointPromptText => "请输入 Azure OpenAI Endpoint（如 https://your-resource.openai.azure.com）";
    public override string? EndpointRequiredMessage => "Azure OpenAI 必须提供 Endpoint，配置已取消。";

    public override string SerializeAuthCredentials(string apiKey, string? endpoint)
    {
        var authData = new Dictionary<string, string>
        {
            ["endpoint"] = endpoint ?? string.Empty,
            ["apiKey"] = apiKey
        };
        return System.Text.Json.JsonSerializer.Serialize(authData, ConfigJsonContext.Default.DictionaryStringString);
    }
}
