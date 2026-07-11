
namespace JoinCode.Abstractions.Models.Todo;

/// <summary>
/// 优先级枚举 — 统一 Todo 和调度器的优先级
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 TodoPriorityConstants + TodoPriorityExtensions
/// 合并自: TaskPriority (Critical)
/// </summary>
public enum TodoPriority
{
    [EnumValue("low")] Low = 0,
    [EnumValue("medium")] Medium = 1,
    [EnumValue("high")] High = 2,
    [EnumValue("critical")] Critical = 3
}
