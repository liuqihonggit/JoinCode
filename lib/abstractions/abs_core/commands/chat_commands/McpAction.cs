namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /mcp 命令的子动作分类集合。
/// 适用范围: /mcp [status|reconnect|enable|disable]
/// list/ls/create/new/delete/rm/remove 走 CrudAction 复用,不纳入此枚举。
///
/// 使用示例:
/// - FromValue("status")    → McpAction.Status
/// - FromValue("RECONNECT") → McpAction.Reconnect (OrdinalIgnoreCase)
/// - McpAction.Enable.ToValue() → "enable"
/// </summary>
public enum McpAction
{
    /// <summary>查看 MCP 服务器状态</summary>
    [EnumValue("status")] Status,

    /// <summary>重连指定 MCP 服务器</summary>
    [EnumValue("reconnect")] Reconnect,

    /// <summary>启用指定 MCP 服务器</summary>
    [EnumValue("enable")] Enable,

    /// <summary>禁用指定 MCP 服务器</summary>
    [EnumValue("disable")] Disable,
}
