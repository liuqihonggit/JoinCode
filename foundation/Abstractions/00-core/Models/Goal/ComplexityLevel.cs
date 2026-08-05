namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 任务复杂度档次
/// [EnumValue] 由 EnumMetadataGenerator 自动生成 ComplexityLevelConstants + ComplexityLevelExtensions
/// </summary>
public enum ComplexityLevel
{
    [EnumValue("low")] Low,
    [EnumValue("medium")] Medium,
    [EnumValue("high")] High,
}
