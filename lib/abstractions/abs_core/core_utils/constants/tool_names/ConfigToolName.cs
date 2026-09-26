namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 配置工具名称枚举 — 对应 ToolCategory.Config
/// </summary>
public enum ConfigToolName {
    /// <summary>
    /// 配置统一入口（省略 value = GET，提供 value = SET）。
    /// ⚠️ 未实现为独立工具，当前由 config_get/config_set 分流实现。保留枚举值供未来统一入口预留。
    /// </summary>
    [EnumValue("config")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    Config,

    /// <summary>
    /// 获取配置设置值 — 只读，已标 ConcurrencySafe=true
    /// </summary>
    [EnumValue("config_get")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ConfigGet,

    /// <summary>
    /// 设置配置设置值 — 写操作，禁止并发
    /// </summary>
    [EnumValue("config_set")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    ConfigSet,

    /// <summary>
    /// 列出所有可配置项 — 只读，已标 ConcurrencySafe=true
    /// </summary>
    [EnumValue("config_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    ConfigList,
}
