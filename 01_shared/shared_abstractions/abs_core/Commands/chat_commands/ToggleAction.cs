namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// 聊天命令子操作:开关类(开/关/状态)
/// 适用于具有 on/off/status 三态的命令(如 /proactive, /mcp status, /ide status, /chrome status, /bridge status)
/// 不适用于有更多动作的命令(如 /proactive 包含 pause/resume, 需保留独立 case)
///
/// 使用示例:
/// - FromValue("on")  → ToggleAction.On
/// - FromValue("OFF") → ToggleAction.Off (OrdinalIgnoreCase)
/// - ToggleAction.Status.ToValue() → "status"
/// </summary>
public enum ToggleAction
{
    /// <summary>开启/激活</summary>
    [EnumValue("on")] On,

    /// <summary>关闭/停用</summary>
    [EnumValue("off")] Off,

    /// <summary>查询当前状态</summary>
    [EnumValue("status")] Status,
}
