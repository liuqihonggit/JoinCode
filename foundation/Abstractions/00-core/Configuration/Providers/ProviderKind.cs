
namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// LLM Provider 类型枚举 — 替代硬编码字符串，编译时类型安全
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 ProviderKindConstants + ProviderKindExtensions
/// </summary>
public enum ProviderKind
{
    [EnumValue("openai")] OpenAI = 0,
    [EnumValue("azure")] Azure = 1,
    [EnumValue("anthropic")] Anthropic = 2,
    [EnumValue("agnes")] Agnes = 3,
    [EnumValue("deepseek")] DeepSeek = 4
}
