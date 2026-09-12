namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /plan 命令子操作集合。
/// 适用范围: /plan [on|off|status|open|toggle]
/// 注: 5 个 case 中 on/enter/off/exit/status 与 ToggleAction 有重叠,
///     但 open/toggle 是 Plan 专属动作,合并为单一枚举避免双重 case 复杂度
///
/// 使用示例:
/// - FromValue("on")    → PlanSubCommand.On
/// - FromValue("ENTER") → PlanSubCommand.On (OrdinalIgnoreCase)
/// - PlanSubCommand.Open.ToValue() → "open"
/// </summary>
public enum PlanSubCommand
{
    /// <summary>进入计划模式</summary>
    [EnumValue("on")] On,

    /// <summary>进入计划模式(别名)</summary>
    [EnumValue("enter")] Enter,

    /// <summary>退出计划模式</summary>
    [EnumValue("off")] Off,

    /// <summary>退出计划模式(别名)</summary>
    [EnumValue("exit")] Exit,

    /// <summary>查询当前计划模式状态</summary>
    [EnumValue("status")] Status,

    /// <summary>在外部编辑器中打开当前计划文件</summary>
    [EnumValue("open")] Open,

    /// <summary>切换计划模式(无参时默认行为)</summary>
    [EnumValue("toggle")] Toggle,
}
