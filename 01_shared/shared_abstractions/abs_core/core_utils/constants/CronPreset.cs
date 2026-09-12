namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Cron 预设表达式枚举 — 统一常见 cron 表达式常量，消除硬编码
/// </summary>
public enum CronPreset
{
    [EnumValue("*/5 * * * *")] Every5Minutes,
    [EnumValue("*/10 * * * *")] Every10Minutes,
    [EnumValue("*/15 * * * *")] Every15Minutes,
    [EnumValue("*/30 * * * *")] Every30Minutes,
    [EnumValue("0 * * * *")] EveryHour,
    [EnumValue("0 */2 * * *")] Every2Hours,
    [EnumValue("0 */6 * * *")] Every6Hours,
    [EnumValue("0 9 * * *")] EveryDayAt9,
    [EnumValue("0 0 * * *")] EveryDayAtMidnight,
    [EnumValue("0 9 * * 1-5")] EveryWeekdayAt9,
    [EnumValue("0 9 * * 1")] EveryMondayAt9,
    [EnumValue("0 0 1 * *")] EveryMonthOnFirst,
}
