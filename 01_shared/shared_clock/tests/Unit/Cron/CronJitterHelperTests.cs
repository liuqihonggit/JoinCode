
namespace Core.Tests.Scheduling.Cron;

public class CronJitterHelperTests
{
    [Fact]
    public void NextCronRunMs_DailyAtNine_ReturnsNextDay()
    {
        // 从 2024-01-01 08:00 开始，下次应该是 2024-01-01 09:00
        var from = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var result = CronJitterHelper.NextCronRunMs("0 9 * * *", from.ToUnixTimeMilliseconds());

        Assert.NotNull(result);
        var resultTime = DateTimeOffset.FromUnixTimeMilliseconds(result.Value);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero), resultTime);
    }

    [Fact]
    public void NextCronRunMs_DailyAtNineAfterNine_ReturnsNextDay()
    {
        // 从 2024-01-01 10:00 开始，下次应该是 2024-01-02 09:00
        var from = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var result = CronJitterHelper.NextCronRunMs("0 9 * * *", from.ToUnixTimeMilliseconds());

        Assert.NotNull(result);
        var resultTime = DateTimeOffset.FromUnixTimeMilliseconds(result.Value);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 9, 0, 0, TimeSpan.Zero), resultTime);
    }

    [Fact]
    public void NextCronRunMs_EveryFiveMinutes_ReturnsNextFiveMinuteMark()
    {
        // 从 08:03 开始，下次应该是 08:05
        var from = new DateTimeOffset(2024, 1, 1, 8, 3, 0, TimeSpan.Zero);
        var result = CronJitterHelper.NextCronRunMs("*/5 * * * *", from.ToUnixTimeMilliseconds());

        Assert.NotNull(result);
        var resultTime = DateTimeOffset.FromUnixTimeMilliseconds(result.Value);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 8, 5, 0, TimeSpan.Zero), resultTime);
    }

    [Fact]
    public void NextCronRunMs_WeekdaysOnly_SkipsWeekend()
    {
        // 从周五 10:00 开始，下次应该是下周一 09:00
        var from = new DateTimeOffset(2024, 1, 5, 10, 0, 0, TimeSpan.Zero);  // 周五
        var result = CronJitterHelper.NextCronRunMs("0 9 * * 1-5", from.ToUnixTimeMilliseconds());

        Assert.NotNull(result);
        var resultTime = DateTimeOffset.FromUnixTimeMilliseconds(result.Value);
        Assert.Equal(new DateTimeOffset(2024, 1, 8, 9, 0, 0, TimeSpan.Zero), resultTime);  // 周一
    }

    [Fact]
    public void NextCronRunMs_InvalidCron_ReturnsNull()
    {
        var result = CronJitterHelper.NextCronRunMs("invalid", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Assert.Null(result);
    }

    [Fact]
    public void JitteredNextCronRunMs_RecurringTask_AddsJitter()
    {
        var from = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var taskId = "abcd1234";

        var baseResult = CronJitterHelper.NextCronRunMs("0 9 * * *", from.ToUnixTimeMilliseconds());
        var jitteredResult = CronJitterHelper.JitteredNextCronRunMs("0 9 * * *", from.ToUnixTimeMilliseconds(), taskId);

        Assert.NotNull(baseResult);
        Assert.NotNull(jitteredResult);
        // 抖动后的时间应该 >= 基础时间
        Assert.True(jitteredResult >= baseResult);
    }

    [Fact]
    public void OneShotJitteredNextCronRunMs_OneShotTask_MaySubtractJitter()
    {
        var from = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var taskId = "abcd1234";

        var baseResult = CronJitterHelper.NextCronRunMs("0 9 * * *", from.ToUnixTimeMilliseconds());
        var jitteredResult = CronJitterHelper.OneShotJitteredNextCronRunMs("0 9 * * *", from.ToUnixTimeMilliseconds(), taskId);

        Assert.NotNull(baseResult);
        Assert.NotNull(jitteredResult);
        // 一次性任务的抖动可能提前触发，但不应该早于 from 时间
        Assert.True(jitteredResult >= from.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void ComputeNextCronRun_MatchesAllFields_ReturnsCorrectTime()
    {
        var fields = new CronFields
        {
            Minute = new[] { 30 },
            Hour = new[] { 14, 15 },
            DayOfMonth = Enumerable.Range(1, 31).ToArray(),
            Month = Enumerable.Range(1, 12).ToArray(),
            DayOfWeek = Enumerable.Range(0, 7).ToArray()
        };

        var from = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var result = CronJitterHelper.ComputeNextCronRun(fields, from);

        Assert.NotNull(result);
        Assert.Equal(14, result.Value.Hour);
        Assert.Equal(30, result.Value.Minute);
    }
}
