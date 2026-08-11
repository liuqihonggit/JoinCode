
namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// LLM 供应商枚举 — 身份/auth key/模型列表/显示名
/// 与 ProtocolKind 分离：供应商决定"发给谁"，协议决定"怎么发请求"
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 VendorKindConstants + VendorKindExtensions
/// </summary>
public enum VendorKind
{
    [EnumValue("openai")] OpenAi = 0,
    [EnumValue("anthropic")] Anthropic = 1,
    [EnumValue("deepseek")] DeepSeek = 2,
    [EnumValue("azure")] Azure = 3,
    [EnumValue("agnes")] Agnes = 4,
    [EnumValue("sensenova")] Sensenova = 5
}
