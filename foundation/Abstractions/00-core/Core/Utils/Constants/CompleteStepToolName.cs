namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 步骤完成工具名称枚举
/// </summary>
public enum CompleteStepToolName
{
    [EnumValue("complete_step")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    CompleteStep,
}
