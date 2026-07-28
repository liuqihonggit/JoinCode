namespace JoinCode.Abstractions.Interfaces.Scheduling;

/// <summary>
/// Cron 调度器抖动配置
/// </summary>
public sealed record CronJitterConfig
{
    /// <summary>
    /// 重复任务前向延迟占间隔的比例
    /// </summary>
    public double RecurringFrac { get; init; } = 0.1;

    /// <summary>
    /// 重复任务前向延迟上限（毫秒）
    /// </summary>
    public long RecurringCapMs { get; init; } = 15 * 60 * 1000;

    /// <summary>
    /// 一次性任务后向提前最大时间（毫秒）
    /// </summary>
    public long OneShotMaxMs { get; init; } = 90 * 1000;

    /// <summary>
    /// 一次性任务后向提前最小时间（毫秒）
    /// </summary>
    public long OneShotFloorMs { get; init; } = 0;

    /// <summary>
    /// 一次性任务触发分钟模数
    /// </summary>
    public int OneShotMinuteMod { get; init; } = WorkflowConstants.Scheduling.OneShotMinuteMod;

    /// <summary>
    /// 重复任务最大存活时间（毫秒），0 表示无限制
    /// </summary>
    public long RecurringMaxAgeMs { get; init; } = 7 * 24 * 60 * 60 * 1000;

    /// <summary>
    /// 默认配置
    /// </summary>
    public static CronJitterConfig Default { get; } = new();
}

/// <summary>
/// Cron 抖动帮助类
/// </summary>
public static class CronJitterHelper
{
    /// <summary>
    /// 计算带抖动的下次触发时间（用于重复任务）
    /// </summary>
    public static long? JitteredNextCronRunMs(
        string cron,
        long fromMs,
        string taskId,
        CronJitterConfig? config = null)
    {
        config ??= CronJitterConfig.Default;

        var t1 = NextCronRunMs(cron, fromMs);
        if (t1 == null) return null;

        var t2 = NextCronRunMs(cron, t1.Value);
        if (t2 == null) return t1;

        var jitter = Math.Min(
            JitterFrac(taskId) * config.RecurringFrac * (t2.Value - t1.Value),
            config.RecurringCapMs);

        return t1.Value + (long)jitter;
    }

    /// <summary>
    /// 计算带抖动的下次触发时间（用于一次性任务）
    /// </summary>
    public static long? OneShotJitteredNextCronRunMs(
        string cron,
        long fromMs,
        string taskId,
        CronJitterConfig? config = null)
    {
        config ??= CronJitterConfig.Default;

        var t1 = NextCronRunMs(cron, fromMs);
        if (t1 == null) return null;

        var fireTime = DateTimeOffset.FromUnixTimeMilliseconds(t1.Value);
        if (fireTime.Minute % config.OneShotMinuteMod != 0)
            return t1;

        var lead = config.OneShotFloorMs +
            JitterFrac(taskId) * (config.OneShotMaxMs - config.OneShotFloorMs);

        return Math.Max(t1.Value - (long)lead, fromMs);
    }

    /// <summary>
    /// 计算下次触发时间
    /// </summary>
    public static long? NextCronRunMs(string cron, long fromMs)
    {
        var fields = CronExpressionParser.Parse(cron);
        if (fields == null) return null;

        var next = ComputeNextCronRun(fields, DateTimeOffset.FromUnixTimeMilliseconds(fromMs));
        return next?.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 计算下次触发时间
    /// </summary>
    public static DateTimeOffset? ComputeNextCronRun(CronFields fields, DateTimeOffset from)
    {
        var minuteSet = new HashSet<int>(fields.Minute);
        var hourSet = new HashSet<int>(fields.Hour);
        var domSet = new HashSet<int>(fields.DayOfMonth);
        var monthSet = new HashSet<int>(fields.Month);
        var dowSet = new HashSet<int>(fields.DayOfWeek);

        var domWild = fields.DayOfMonth.Length == 31;
        var dowWild = fields.DayOfWeek.Length == 7;

        var t = from.AddMinutes(1);
        t = new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);

        const int maxIter = 366 * 24 * 60;

        for (int i = 0; i < maxIter; i++)
        {
            var month = t.Month;
            if (!monthSet.Contains(month))
            {
                t = new DateTimeOffset(t.Year, t.Month, 1, 0, 0, 0, t.Offset).AddMonths(1);
                continue;
            }

            var dom = t.Day;
            var dow = (int)t.DayOfWeek;

            bool dayMatches = domWild && dowWild
                ? true
                : domWild
                    ? dowSet.Contains(dow)
                    : dowWild
                        ? domSet.Contains(dom)
                        : domSet.Contains(dom) || dowSet.Contains(dow);

            if (!dayMatches)
            {
                t = new DateTimeOffset(t.Year, t.Month, t.Day, 0, 0, 0, t.Offset).AddDays(1);
                continue;
            }

            if (!hourSet.Contains(t.Hour))
            {
                t = new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset).AddHours(1);
                continue;
            }

            if (!minuteSet.Contains(t.Minute))
            {
                t = t.AddMinutes(1);
                continue;
            }

            return t;
        }

        return null;
    }

    /// <summary>
    /// 从任务 ID 计算抖动比例（0-1）
    /// </summary>
    private static double JitterFrac(string taskId)
    {
        if (string.IsNullOrEmpty(taskId) || taskId.Length < 8)
            return 0;

        var hex = taskId[..8];
        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
        {
            return value / (double)uint.MaxValue;
        }

        return 0;
    }
}
