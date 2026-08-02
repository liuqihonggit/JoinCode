namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Cron 调度工具名称枚举
/// </summary>
public enum CronToolName
{
    [EnumValue("CronCreate")] CronCreate,
    [EnumValue("CronList")] CronList,
    [EnumValue("CronDelete")] CronDelete,
    [EnumValue("cron_validate")] CronValidate,
}
