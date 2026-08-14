
namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// LLM Provider 配置类 - 支持多 Provider（OpenAI/Azure/Anthropic/Agnes）
/// </summary>
public class ProviderConfig
{
    /// <summary>
    /// 供应商身份 — openai/anthropic/deepseek/azure/agnes/sensenova
    /// 决定 auth key/模型列表/显示名
    /// </summary>
    [Required]
    public string Vendor { get; set; } = VendorKind.DeepSeek.ToValue();

    /// <summary>
    /// 协议 — openai-compatible/anthropic/azure/agnes
    /// 决定 API 格式/认证/端点路径/QueryService 分派
    /// </summary>
    [Required]
    public string Protocol { get; set; } = ProtocolKind.OpenAiCompatible.ToValue();

    /// <summary>
    /// API Key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 模型 ID
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// API 端点（Azure/商汤等需要）
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
    /// 供应商枚举 — 从 Vendor 字符串推导
    /// </summary>
    public VendorKind VendorKind => VendorKindExtensions.FromValue(Vendor) ?? VendorKind.DeepSeek;

    /// <summary>
    /// 协议枚举 — 从 Protocol 字符串推导，决定 QueryService 分派
    /// </summary>
    public ProtocolKind ProtocolKind => ProtocolKindExtensions.FromValue(Protocol) ?? ProtocolKind.OpenAiCompatible;

    /// <summary>
    /// Provider 完整定义 — 由 ConfigLoader 在加载时注入，QueryService 等消费者通过此属性访问 Provider 知识
    /// </summary>
    public IProviderDefinition? Definition { get; set; }
}
