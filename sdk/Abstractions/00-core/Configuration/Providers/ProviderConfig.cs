
namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// LLM Provider 配置类 - 支持多 Provider（OpenAI/Azure/Anthropic/Agnes）
/// </summary>
public class ProviderConfig
{
    /// <summary>
    /// Provider 类型: openai, azure, anthropic, agnes
    /// </summary>
    [Required]
    public string Provider { get; set; } = ProviderKind.OpenAI.ToValue();

    /// <summary>
    /// API Key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 模型 ID
    /// </summary>
    public string ModelId { get; set; } = CanonicalModel.Gpt4o.ToValue();

    /// <summary>
    /// API 端点（Azure 需要）
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// 是否启用 OAuth Token 支持（Anthropic）
    /// </summary>
    public bool EnableOAuthTokenSupport { get; set; } = false;

    /// <summary>
    /// 组织 ID（OpenAI 可选）
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// API 版本（Azure 可选）
    /// </summary>
    public string? ApiVersion { get; set; } = "2024-02-01";

    /// <summary>
    /// 从 Provider 字符串推导的 ProviderKind 枚举 — 替代字符串比较，编译时类型安全
    /// </summary>
    public ProviderKind Kind => ProviderKindExtensions.FromValue(Provider) ?? ProviderKind.OpenAI;

    /// <summary>
    /// Provider 完整定义 — 由 ConfigLoader 在加载时注入，QueryService 等消费者通过此属性访问 Provider 知识
    /// </summary>
    public IProviderDefinition? Definition { get; set; }
}
