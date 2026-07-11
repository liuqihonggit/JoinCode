
namespace Core.CostTracking;

/// <summary>
/// 成本告警信息类
/// </summary>
public sealed class CostAlert
{
    /// <summary>
    /// 告警级别
    /// </summary>
    [JsonPropertyName("level")]
    public required CostAlertLevel Level { get; init; }

    /// <summary>
    /// 告警消息
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// 告警时间戳
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// 当前成本
    /// </summary>
    [JsonPropertyName("currentCost")]
    public required decimal CurrentCost { get; init; }

    /// <summary>
    /// 预算限额
    /// </summary>
    [JsonPropertyName("budgetLimit")]
    public required decimal BudgetLimit { get; init; }

    /// <summary>
    /// 已使用预算百分比 (0.0 - 1.0)
    /// </summary>
    [JsonPropertyName("percentageUsed")]
    public required double PercentageUsed { get; init; }

    /// <summary>
    /// 创建一个新的成本告警
    /// </summary>
    public static CostAlert Create(
        CostAlertLevel level,
        string message,
        decimal currentCost,
        decimal budgetLimit)
    {
        var percentageUsed = budgetLimit > 0
            ? (double)(currentCost / budgetLimit)
            : 0.0;

        return new CostAlert
        {
            Level = level,
            Message = message,
            Timestamp = DateTime.UtcNow,
            CurrentCost = currentCost,
            BudgetLimit = budgetLimit,
            PercentageUsed = Math.Min(percentageUsed, 1.0)
        };
    }
}
