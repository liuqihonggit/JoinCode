
namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// Provider 完整定义接口 — 每个 Provider 实现自己的全部知识
/// 包含：配置解析 + 模型列表 + 别名映射 + 能力判断
/// </summary>
public interface IProviderDefinition
{
    /// <summary>
    /// Provider 枚举类型
    /// </summary>
    ProviderKind Kind { get; }

    /// <summary>
    /// Provider 标识名（小写，如 "openai", "agnes"）
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 显示名称（如 "OpenAI", "Azure OpenAI"）
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 默认模型 ID
    /// </summary>
    string DefaultModelId { get; }

    /// <summary>
    /// 默认快速模型 ID
    /// </summary>
    string DefaultFastModelId { get; }

    /// <summary>
    /// 默认端点（null 表示无默认端点）
    /// </summary>
    string? DefaultEndpoint { get; }

    /// <summary>
    /// 可用模型列表
    /// </summary>
    IReadOnlyList<ModelEntry> AvailableModels { get; }

    /// <summary>
    /// 从环境变量解析 API Key（优先级最高）
    /// </summary>
    string? ResolveApiKeyFromEnv();

    /// <summary>
    /// Provider 专属环境变量名（如 "OPENAI_API_KEY"）
    /// </summary>
    string? ApiKeyEnvironmentVariable { get; }

    /// <summary>
    /// Provider 额外端点环境变量名（如 Azure 的 "AZURE_OPENAI_ENDPOINT"），null 表示无
    /// </summary>
    string? EndpointEnvironmentVariable { get; }

    /// <summary>
    /// 默认 API 版本（仅 Azure 等需要，null 表示无）
    /// </summary>
    string? DefaultApiVersion => null;

    /// <summary>
    /// 从环境变量解析额外端点（仅 Azure 等需要）
    /// </summary>
    string? ResolveEndpointFromEnv() => null;

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    bool IsValid(ProviderConfig config);

    /// <summary>
    /// 解析模型别名（如 "sonnet" → "claude-sonnet-4-6"）
    /// </summary>
    string? ResolveAlias(string input) => null;

    /// <summary>
    /// 检查模型是否支持 Fast Mode
    /// </summary>
    bool SupportsFastMode(string modelId) => true;

    /// <summary>
    /// 检查模型是否支持 Effort
    /// </summary>
    bool SupportsEffort(string modelId) => false;

    /// <summary>
    /// 检查模型是否支持 max Effort
    /// </summary>
    bool SupportsMaxEffort(string modelId) => false;

    /// <summary>
    /// 构建 API 基础 URL（责任链：每个 Provider 自己决定 URL 格式）
    /// </summary>
    string GetBaseUrl(ProviderConfig config);

    /// <summary>
    /// 获取 Chat API 相对路径（如 "chat/completions", "v1/messages"）
    /// </summary>
    string GetChatEndpoint(ProviderConfig config);

    /// <summary>
    /// 配置 HttpClient 的 Provider 专属 Header（如 Anthropic 的 x-api-key）
    /// </summary>
    void ConfigureHttpClient(HttpClient client, ProviderConfig config);

    /// <summary>
    /// 是否支持 OAuth 登录
    /// </summary>
    bool SupportsOAuth => false;

    /// <summary>
    /// 获取 OAuth 配置（仅 SupportsOAuth 为 true 时有效）
    /// </summary>
    OAuthConfig? GetOAuthConfig() => null;

    /// <summary>
    /// 是否支持Web搜索（Anthropic服务端搜索等）
    /// </summary>
    bool SupportsWebSearch => false;

    /// <summary>
    /// 判断 auth.json 中的凭证是否为复合格式（如 Azure 存储了 JSON 对象而非纯 API Key）
    /// </summary>
    bool IsCompoundAuthFormat(string apiKey) => false;

    /// <summary>
    /// 从复合格式凭证中提取 API Key（如 Azure 的 JSON 对象中的 apiKey 字段）
    /// </summary>
    string? ExtractApiKeyFromCompound(string apiKey) => null;

    /// <summary>
    /// 登录/初始配置时是否需要交互式收集 Endpoint — 替代 `if (provider == "azure")` 硬编码
    /// Azure 覆写为 true；其余供应商默认 false（即使空实现也必须严格覆写判断）
    /// </summary>
    bool RequiresInteractiveEndpoint => false;

    /// <summary>
    /// 序列化保存到 auth.json 的凭证字符串 — 替代 LoginCommand 中的 Azure 复合 JSON 硬编码
    /// 默认直接返回 apiKey；Azure 覆写为 JSON 对象含 endpoint + apiKey
    /// </summary>
    /// <param name="apiKey">用户输入的 API Key</param>
    /// <param name="endpoint">交互式收集的 Endpoint（仅 RequiresInteractiveEndpoint 为 true 时有值）</param>
    string SerializeAuthCredentials(string apiKey, string? endpoint) => apiKey;

    /// <summary>
    /// Endpoint 提示文案 — 替代硬编码 "请输入 Azure OpenAI Endpoint"
    /// </summary>
    string? EndpointPromptText => null;

    /// <summary>
    /// Endpoint 校验失败时的提示文案
    /// </summary>
    string? EndpointRequiredMessage => null;
}
