namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /config 命令子操作集合。
/// 适用范围: /config [get|set|list|remove]
/// get/set 为 Config 专属(读取/写入配置项),list/remove 复用 CRUD 语义但纳入此枚举统一管理特性参数。
///
/// 使用示例:
/// - FromValue("get")    → ConfigAction.Get
/// - FromValue("SET")    → ConfigAction.Set (OrdinalIgnoreCase)
/// - ConfigAction.List.ToValue() → "list"
/// </summary>
public enum ConfigAction {
    /// <summary>获取单个配置项的值</summary>
    [EnumValue("get")] Get,

    /// <summary>设置配置项的值</summary>
    [EnumValue("set")] Set,

    /// <summary>列出所有配置项</summary>
    [EnumValue("list")] List,

    /// <summary>移除配置项</summary>
    [EnumValue("remove")] Remove,
}
