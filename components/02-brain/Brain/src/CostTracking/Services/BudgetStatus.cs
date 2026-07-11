
namespace Core.CostTracking;

/// <summary>
/// 预算状态类
/// </summary>
public sealed class BudgetStatus
{
    /// <summary>
    /// 今日已使用金额 (USD)
    /// </summary>
    [JsonPropertyName("dailyUsed")]
    public required decimal DailyUsed { get; init; }

    /// <summary>
    /// 每日预算限额 (USD)
    /// </summary>
    [JsonPropertyName("dailyLimit")]
    public required decimal DailyLimit { get; init; }

    /// <summary>
    /// 本月已使用金额 (USD)
    /// </summary>
    [JsonPropertyName("monthlyUsed")]
    public required decimal MonthlyUsed { get; init; }

    /// <summary>
    /// 每月预算限额 (USD)
    /// </summary>
    [JsonPropertyName("monthlyLimit")]
    public required decimal MonthlyLimit { get; init; }

    /// <summary>
    /// 是否超出每日预算
    /// </summary>
    [JsonPropertyName("isDailyExceeded")]
    public bool IsDailyExceeded => DailyLimit > 0 && DailyUsed >= DailyLimit;

    /// <summary>
    /// 是否超出每月预算
    /// </summary>
    [JsonPropertyName("isMonthlyExceeded")]
    public bool IsMonthlyExceeded => MonthlyLimit > 0 && MonthlyUsed >= MonthlyLimit;

    /// <summary>
    /// 获取每日剩余预算
    /// </summary>
    public decimal GetDailyRemainingBudget()
    {
        if (DailyLimit <= 0)
        {
            return decimal.MaxValue;
        }

        return Math.Max(0, DailyLimit - DailyUsed);
    }

    /// <summary>
    /// 获取每月剩余预算
    /// </summary>
    public decimal GetMonthlyRemainingBudget()
    {
        if (MonthlyLimit <= 0)
        {
            return decimal.MaxValue;
        }

        return Math.Max(0, MonthlyLimit - MonthlyUsed);
    }

    /// <summary>
    /// 获取每日预算使用百分比 (0.0 - 1.0)
    /// </summary>
    public double GetDailyUsagePercentage()
    {
        if (DailyLimit <= 0)
        {
            return 0.0;
        }

        return Math.Min(1.0, (double)(DailyUsed / DailyLimit));
    }

    /// <summary>
    /// 获取每月预算使用百分比 (0.0 - 1.0)
    /// </summary>
    public double GetMonthlyUsagePercentage()
    {
        if (MonthlyLimit <= 0)
        {
            return 0.0;
        }

        return Math.Min(1.0, (double)(MonthlyUsed / MonthlyLimit));
    }

    /// <summary>
    /// 获取剩余预算（取每日和每月剩余的最小值）
    /// </summary>
    public decimal GetRemainingBudget()
    {
        var dailyRemaining = GetDailyRemainingBudget();
        var monthlyRemaining = GetMonthlyRemainingBudget();

        if (dailyRemaining == decimal.MaxValue)
        {
            return monthlyRemaining;
        }

        if (monthlyRemaining == decimal.MaxValue)
        {
            return dailyRemaining;
        }

        return Math.Min(dailyRemaining, monthlyRemaining);
    }

    /// <summary>
    /// 检查是否超出任何预算限制
    /// </summary>
    public bool IsAnyBudgetExceeded()
    {
        return IsDailyExceeded || IsMonthlyExceeded;
    }

    /// <summary>
    /// 获取预算状态摘要
    /// </summary>
    public string GetStatusSummary()
    {
        var dailyPercent = GetDailyUsagePercentage() * 100;
        var monthlyPercent = GetMonthlyUsagePercentage() * 100;

        return $"每日: ${DailyUsed:F2} / ${DailyLimit:F2} ({dailyPercent:F1}%), " +
               $"每月: ${MonthlyUsed:F2} / ${MonthlyLimit:F2} ({monthlyPercent:F1}%)";
    }
}
