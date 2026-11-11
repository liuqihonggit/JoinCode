namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// Bedrock 跨区域推理前缀 — 对齐 TS BEDROCK_REGION_PREFIXES
/// <para>us: 美国区域, eu: 欧洲区域, apac: 亚太区域, global: 全球</para>
/// </summary>
public enum BedrockRegionPrefix {
    [EnumValue("us")] Us,
    [EnumValue("eu")] Eu,
    [EnumValue("apac")] Apac,
    [EnumValue("global")] Global,
}
