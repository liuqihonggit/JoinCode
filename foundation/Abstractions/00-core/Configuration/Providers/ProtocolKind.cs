
namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// LLM 协议枚举 — API 格式/认证/端点路径，决定 QueryService 分派
/// 与 VendorKind 分离：协议决定"怎么发请求"，供应商决定"发给谁"
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 ProtocolKindConstants + ProtocolKindExtensions
/// </summary>
public enum ProtocolKind
{
    [EnumValue("openai-compatible")] OpenAiCompatible = 0,
    [EnumValue("anthropic")] Anthropic = 1,
    [EnumValue("azure")] Azure = 2,
    [EnumValue("agnes")] Agnes = 3
}
