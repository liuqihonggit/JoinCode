namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 计划模式工具名称枚举
/// </summary>
public enum PlanToolName
{
    [EnumValue("plan_mode_start")] PlanModeStart,
    [EnumValue("plan_mode_end")] PlanModeEnd,
    [EnumValue("plan_mode_status")] PlanModeStatus,
    [EnumValue("EnterPlanMode")] EnterPlanMode,
    [EnumValue("ExitPlanMode")] ExitPlanMode,
    [EnumValue("get_plan_status")] GetPlanStatus,
    [EnumValue("add_plan_step")] AddPlanStep,
    [EnumValue("approve_plan_step")] ApprovePlanStep,
    [EnumValue("reject_plan_step")] RejectPlanStep,
    [EnumValue("execute_plan_steps")] ExecutePlanSteps,
    [EnumValue("modify_plan_step")] ModifyPlanStep,
    [EnumValue("remove_plan_step")] RemovePlanStep,
    [EnumValue("get_plan_history")] GetPlanHistory,
}
