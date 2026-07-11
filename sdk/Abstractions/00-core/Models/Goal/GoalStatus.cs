
namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 目标状态枚举
/// </summary>
public enum GoalStatus
{
    /// <summary>正在执行</summary>
    [EnumValue("pursuing")] Pursuing,
    /// <summary>已暂停</summary>
    [EnumValue("paused")] Paused,
    /// <summary>已完成</summary>
    [EnumValue("achieved")] Achieved,
    /// <summary>无法完成</summary>
    [EnumValue("unmet")] Unmet,
    /// <summary>预算耗尽</summary>
    [EnumValue("budget_limited")] BudgetLimited
}
