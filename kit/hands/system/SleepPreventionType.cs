
namespace Services.SystemPower;

/// <summary>
/// 防睡眠类型 — 指定 SetThreadExecutionState 的工作模式
/// </summary>
public enum SleepPreventionType
{
    /// <summary>
    /// 连续防睡眠 — 持续阻止系统进入睡眠,直到显式调用 AllowSleepAsync
    /// </summary>
    [EnumValue("continuous")] Continuous,
    /// <summary>
    /// 一次性防睡眠 — 仅阻止当前一次睡眠触发,系统可再次尝试进入睡眠
    /// </summary>
    [EnumValue("oneTime")] OneTime
}
