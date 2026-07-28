
namespace Core.CostTracking;

/// <summary>
/// 成本告警事件参数类
/// </summary>
public sealed class CostAlertEventArgs : EventArgs
{
    /// <summary>
    /// 成本告警信息
    /// </summary>
    [JsonPropertyName("alert")]
    public required CostAlert Alert { get; init; }

    /// <summary>
    /// 创建成本告警事件参数
    /// </summary>
    /// <param name="alert">成本告警信息</param>
    /// <returns>事件参数实例</returns>
    public static CostAlertEventArgs Create(CostAlert alert)
    {
        return new CostAlertEventArgs
        {
            Alert = alert
        };
    }
}
