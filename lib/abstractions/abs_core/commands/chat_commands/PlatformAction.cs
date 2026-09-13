namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// 平台集成命令的子动作分类集合。
/// 适用范围: /chrome [connect|disconnect|install|toggle|status] + /ide [detect|connect|disconnect|open|status] + /mobile [start|stop|url]
/// 3 个命令的 case 字符串 union,跨命令共享同一语义集合(connect/disconnect/status 3 个动作被 chrome+ide 同时使用)。
///
/// 使用示例:
/// - FromValue("connect")     → PlatformAction.Connect
/// - FromValue("INSTALL")     → PlatformAction.Install (OrdinalIgnoreCase)
/// - PlatformAction.Status.ToValue() → "status"
/// </summary>
public enum PlatformAction
{
    /// <summary>建立连接 (chrome connect + ide connect 共用)</summary>
    [EnumValue("connect")] Connect,

    /// <summary>断开连接 (chrome disconnect + ide disconnect 共用)</summary>
    [EnumValue("disconnect")] Disconnect,

    /// <summary>查看状态 (chrome status + ide status + mobile 默认 null/"" 共用)</summary>
    [EnumValue("status")] Status,

    /// <summary>安装扩展 (chrome 专属)</summary>
    [EnumValue("install")] Install,

    /// <summary>切换默认启用状态 (chrome 专属)</summary>
    [EnumValue("toggle")] Toggle,

    /// <summary>检测已安装的 IDE (ide 专属)</summary>
    [EnumValue("detect")] Detect,

    /// <summary>在 IDE 中打开文件 (ide 专属)</summary>
    [EnumValue("open")] Open,

    /// <summary>启动移动端连接服务 (mobile 专属)</summary>
    [EnumValue("start")] Start,

    /// <summary>停止移动端连接服务 (mobile 专属)</summary>
    [EnumValue("stop")] Stop,

    /// <summary>获取移动端连接 URL (mobile 专属)</summary>
    [EnumValue("url")] Url,
}
