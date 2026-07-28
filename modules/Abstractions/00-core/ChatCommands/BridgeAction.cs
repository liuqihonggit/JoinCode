namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /bridge 命令的子动作分类集合。
/// 适用范围: /bridge [qr|sessions|status|connect|disconnect]
/// connect/disconnect 与 PlatformAction 语义相同但属于 Bridge 子系统独立管理。
///
/// 使用示例:
/// - FromValue("qr")        → BridgeAction.Qr
/// - FromValue("SESSIONS")  → BridgeAction.Sessions (OrdinalIgnoreCase)
/// - BridgeAction.Status.ToValue() → "status"
/// </summary>
public enum BridgeAction
{
    /// <summary>显示 QR 码用于移动端连接</summary>
    [EnumValue("qr")] Qr,

    /// <summary>列出活跃 Bridge 会话</summary>
    [EnumValue("sessions")] Sessions,

    /// <summary>查看 Bridge 状态</summary>
    [EnumValue("status")] Status,

    /// <summary>启动 Bridge 客户端连接</summary>
    [EnumValue("connect")] Connect,

    /// <summary>断开 Bridge 客户端连接</summary>
    [EnumValue("disconnect")] Disconnect,
}
