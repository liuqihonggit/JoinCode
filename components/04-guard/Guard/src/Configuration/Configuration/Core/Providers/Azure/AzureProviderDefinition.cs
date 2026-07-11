
namespace Core.Configuration.Providers;

/// <summary>
/// Azure OpenAI Provider 完整定义 — 继承 OpenAICompatible 基类共享模型列表和别名映射
/// Azure 专属差异：api-key Header / 部署路径 URL / api-version / OAuth / 复合凭证
/// </summary>
public sealed class AzureProviderDefinition : OpenAICompatibleProviderDefinitionBase
{
    public override ProviderKind Kind => ProviderKind.Azure;
    public override string ProviderName => ProviderKind.Azure.ToValue();
    public override string DisplayName => "Azure OpenAI";
    public override string DefaultModelId => CanonicalModel.Gpt4o.ToValue();
    public override string DefaultFastModelId => CanonicalModel.Gpt4oMini.ToValue();
    public override string? DefaultEndpoint => null;
    public override string? ApiKeyEnvironmentVariable => ProviderEnvVar.AzureOpenAiApiKey.ToValue();
    public override string? EndpointEnvironmentVariable => ProviderEnvVar.AzureOpenAiEndpoint.ToValue();
    public override string? DefaultApiVersion => "2024-02-01";

    public override string GetBaseUrl(ProviderConfig config)
    {
        return $"{config.Endpoint?.TrimEnd('/')}/openai/deployments/{config.ModelId}";
    }

    /// <summary>
    /// Azure: 部署路径下的 chat/completions
    /// </summary>
    public override string GetChatEndpoint(ProviderConfig config)
    {
        return $"chat/completions?api-version={config.ApiVersion}";
    }

    /// <summary>
    /// Azure: api-key Header 认证
    /// </summary>
    public override void ConfigureHttpClient(HttpClient client, ProviderConfig config)
    {
        if (!string.IsNullOrEmpty(config.ApiKey))
            client.DefaultRequestHeaders.Add("api-key", config.ApiKey);
    }

    public override string? ResolveApiKeyFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiApiKey.ToValue());
    }

    /// <summary>
    /// Azure 额外从环境变量读取端点
    /// </summary>
    public override string? ResolveEndpointFromEnv()
    {
        return Environment.GetEnvironmentVariable(ProviderEnvVar.AzureOpenAiEndpoint.ToValue());
    }

    public override bool IsValid(ProviderConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.ApiKey) && !string.IsNullOrWhiteSpace(config.Endpoint);
    }

    /// <summary>
    /// Azure 在 auth.json 中存储 JSON 对象（含 endpoint + apiKey），以 { 开头
    /// </summary>
    public override bool IsCompoundAuthFormat(string apiKey) => apiKey.StartsWith("{");

    /// <summary>
    /// Azure 支持 OAuth 登录
    /// </summary>
    public override bool SupportsOAuth => true;

    /// <summary>
    /// Azure OAuth 配置
    /// </summary>
    public override OAuthConfig? GetOAuthConfig() => new()
    {
        Provider = "azure",
        ClientId = Environment.GetEnvironmentVariable(JccEnvVar.AzureClientId.ToValue()) ?? "",
        AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        RedirectUri = "http://localhost:5000/oauth/callback",
        Scope = new List<string> { "https://cognitiveservices.azure.com/.default" }
    };

    /// <summary>
    /// 从 Azure 复合格式中提取 API Key
    /// </summary>
    public override string? ExtractApiKeyFromCompound(string apiKey)
    {
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize(apiKey, ConfigJsonContext.Default.DictionaryStringString);
            return data?.GetValueOrDefault("apiKey");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Azure 登录必须收集 Endpoint
    /// </summary>
    public override bool RequiresInteractiveEndpoint => true;

    /// <summary>
    /// Endpoint 提示文案 — Azure 专属
    /// </summary>
    public override string? EndpointPromptText => "请输入 Azure OpenAI Endpoint（如 https://your-resource.openai.azure.com）";

    /// <summary>
    /// Endpoint 校验失败文案
    /// </summary>
    public override string? EndpointRequiredMessage => "Azure OpenAI 必须提供 Endpoint，配置已取消。";

    /// <summary>
    /// 序列化 Azure 复合凭证（endpoint + apiKey JSON 对象）
    /// </summary>
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
