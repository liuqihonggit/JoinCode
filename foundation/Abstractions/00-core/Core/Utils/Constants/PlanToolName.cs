namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 计划模式工具名称枚举
/// </summary>
public enum PlanToolName
{
    [EnumValue("plan_mode_start")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    PlanModeStart,

    [EnumValue("plan_mode_end")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    PlanModeEnd,

    [EnumValue("plan_mode_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    PlanModeStatus,

    [EnumValue("EnterPlanMode")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    EnterPlanMode,

    [EnumValue("ExitPlanMode")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ExitPlanMode,

    [EnumValue("get_plan_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GetPlanStatus,

    [EnumValue("add_plan_step")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    AddPlanStep,

    [EnumValue("approve_plan_step")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ApprovePlanStep,

    [EnumValue("reject_plan_step")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    RejectPlanStep,

    [EnumValue("execute_plan_steps")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ExecutePlanSteps,

    [EnumValue("modify_plan_step")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ModifyPlanStep,

    [EnumValue("remove_plan_step")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    RemovePlanStep,

    [EnumValue("get_plan_history")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GetPlanHistory,
}
