
namespace JoinCode.Abstractions.Decision;

/// <summary>
/// 类型化决策原语枚举 — Jev 等 System One 模型的决策类型
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 TypedDecisionKindEnumConstants + TypedDecisionKindExtensions
/// </summary>
public enum TypedDecisionKind {
    [EnumValue("noul")] Noul = 0,
    [EnumValue("choice")] Choice = 1,
    [EnumValue("score")] Score = 2
}
