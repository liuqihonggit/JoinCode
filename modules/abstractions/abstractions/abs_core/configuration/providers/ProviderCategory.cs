namespace JoinCode.Abstractions.Configuration.Providers;

/// <summary>
/// 供应商类别枚举 — 统一分类 LLM/Search/Embedding 等能力域（TASK054 §7）。
/// <para>[EnumValue] 特性由 EnumMetadataGenerator 自动生成 ProviderCategoryEnumConstants + ProviderCategoryExtensions。</para>
/// </summary>
public enum ProviderCategory {
    [EnumValue("llm")] Llm = 0,
    [EnumValue("search")] Search = 1,
    [EnumValue("embedding")] Embedding = 2,
}
