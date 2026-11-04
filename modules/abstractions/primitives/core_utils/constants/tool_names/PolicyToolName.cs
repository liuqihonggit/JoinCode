namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 策略工具名称枚举 — 对应 ToolCategory.Policy
/// </summary>
public enum PolicyToolName {
    [EnumValue("policy_check")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PolicyCheck,

    [EnumValue("policy_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PolicyList,
}
