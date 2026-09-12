namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 子任务优先级
/// [EnumValue] 由 EnumMetadataGenerator 自动生成 SubTaskPriorityConstants + SubTaskPriorityExtensions
/// </summary>
public enum SubTaskPriority
{
    [EnumValue("high")] High,
    [EnumValue("medium")] Medium,
    [EnumValue("low")] Low,
}
