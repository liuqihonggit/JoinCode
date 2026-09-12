namespace JoinCode.Abstractions.ChatCommands;

/// <summary>
/// /cost 命令时间范围集合。
/// 适用范围: /cost [today|session|total]
/// 3 个值全 Cost 专属,与 ToggleAction/CrudAction 无重叠,创建独立枚举。
///
/// 使用示例:
/// - FromValue("today")  → CostScope.Today
/// - FromValue("TOTAL")  → CostScope.Total (OrdinalIgnoreCase)
/// - CostScope.Session.ToValue() → "session"
/// </summary>
public enum CostScope
{
    /// <summary>当日累计成本</summary>
    [EnumValue("today")] Today,

    /// <summary>当前会话累计成本(默认 scope,无参时使用)</summary>
    [EnumValue("session")] Session,

    /// <summary>所有时间累计成本</summary>
    [EnumValue("total")] Total,
}
