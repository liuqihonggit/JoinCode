namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 集群执行方案模式
/// [EnumValue] 由 EnumMetadataGenerator 自动生成 ExecutionModeConstants + ExecutionModeExtensions
/// </summary>
public enum ExecutionMode
{
    [EnumValue("A")] PlanA,
    [EnumValue("B")] PlanB,
}
