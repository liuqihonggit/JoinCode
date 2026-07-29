
namespace Core.CostTracking;

/// <summary>
/// 成本告警级别枚举
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CostAlertLevel>))]
public enum CostAlertLevel
{
    /// <summary>
    /// 无告警
    /// </summary>
    [EnumValue("none")] None,

    /// <summary>
    /// 信息级别告警
    /// </summary>
    [EnumValue("info")] Info,

    /// <summary>
    /// 警告级别告警
    /// </summary>
    [EnumValue("warning")] Warning,

    /// <summary>
    /// 严重级别告警
    /// </summary>
    [EnumValue("critical")] Critical
}
