namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Cron 调度工具名称枚举
/// </summary>
public enum CronToolName
{
    [EnumValue("CronCreate")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    CronCreate,

    [EnumValue("CronList")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CronList,

    [EnumValue("CronDelete")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    CronDelete,

    [EnumValue("cron_validate")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CronValidate,
}
