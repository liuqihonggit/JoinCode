
namespace Core.Tests.Scheduling.Cron;

public class CronTaskTests
{
    [Fact]
    public void IsExpired_PermanentTask_ReturnsFalse()
    {
        var task = new CronTask
        {
            Id = "test123",
            CronExpression = "0 9 * * *",
            Prompt = "Test",
            CreatedAt = 0,
            IsRecurring = true,
            IsPermanent = true
        };

        Assert.False(task.IsExpired(long.MaxValue, 7 * 24 * 60 * 60 * 1000));
    }

    [Fact]
    public void IsExpired_NonRecurringTask_ReturnsFalse()
    {
        var task = new CronTask
        {
            Id = "test123",
            CronExpression = "0 9 * * *",
            Prompt = "Test",
            CreatedAt = 0,
            IsRecurring = false,
            IsPermanent = false
        };

        Assert.False(task.IsExpired(long.MaxValue, 7 * 24 * 60 * 60 * 1000));
    }

    [Fact]
    public void IsExpired_RecurringTaskWithinAgeLimit_ReturnsFalse()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-6).ToUnixTimeMilliseconds();
        var task = new CronTask
        {
            Id = "test123",
            CronExpression = "0 9 * * *",
            Prompt = "Test",
            CreatedAt = createdAt,
            IsRecurring = true,
            IsPermanent = false
        };

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.False(task.IsExpired(now, 7 * 24 * 60 * 60 * 1000));  // 7天限制
    }

    [Fact]
    public void IsExpired_RecurringTaskBeyondAgeLimit_ReturnsTrue()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-8).ToUnixTimeMilliseconds();
        var task = new CronTask
        {
            Id = "test123",
            CronExpression = "0 9 * * *",
            Prompt = "Test",
            CreatedAt = createdAt,
            IsRecurring = true,
            IsPermanent = false
        };

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(task.IsExpired(now, 7 * 24 * 60 * 60 * 1000));  // 7天限制
    }

    [Fact]
    public void IsExpired_ZeroMaxAge_ReturnsFalse()
    {
        var createdAt = DateTimeOffset.UtcNow.AddYears(-1).ToUnixTimeMilliseconds();
        var task = new CronTask
        {
            Id = "test123",
            CronExpression = "0 9 * * *",
            Prompt = "Test",
            CreatedAt = createdAt,
            IsRecurring = true,
            IsPermanent = false
        };

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.False(task.IsExpired(now, 0));  // 0 表示无限制
    }
}
