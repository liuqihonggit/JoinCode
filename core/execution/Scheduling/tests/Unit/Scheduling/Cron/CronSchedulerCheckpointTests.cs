
namespace Core.Tests.Scheduling.Cron;

/// <summary>
/// CronScheduler checkpoint 行为测试 — 验证定时任务触发后 MarkTasksFiredAsync 被调用，
/// 且即使 Check 主体抛异常，已触发的 recurring 任务仍会被标记（finally 块防御）。
/// </summary>
public sealed class CronSchedulerCheckpointTests
{
    private static CronTask MakeRecurringTask(string id, string cron = "*/1 * * * *")
    {
        return new CronTask
        {
            Id = id,
            CronExpression = cron,
            Prompt = "test-prompt",
            CreatedAt = 0,
            IsRecurring = true,
            IsPermanent = true
        };
    }

    [Fact]
    public async Task Checkpoint_RecurringTaskFired_ShouldCallMarkTasksFiredAsync()
    {
        var task = MakeRecurringTask("t1");
        var store = new Mock<ICronTaskStore>();
        store.Setup(s => s.GetAllTasksAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<CronTask> { task });

        var markCalled = false;
        store.Setup(s => s.MarkTasksFiredAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
             .Callback(() => markCalled = true)
             .Returns(Task.CompletedTask);

        var fired = false;
        var options = new CronSchedulerOptions
        {
            OnFire = _ => { fired = true; return Task.CompletedTask; },
            CheckIntervalMs = 50
        };

        var fakeTime = new FakeTimeProvider();
        var clock = new Mock<IClockService>();
        clock.SetupGet(c => c.TimeProvider).Returns(fakeTime);
        clock.Setup(c => c.GetUtcNowOffset()).Returns(() => fakeTime.GetUtcNow());

        await using var scheduler = new CronScheduler(options, store.Object, clock.Object);
        await scheduler.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!markCalled && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await scheduler.StopAsync();

        Assert.True(fired, "OnFire 应被调用");
        Assert.True(markCalled, "MarkTasksFiredAsync 应被调用（checkpoint）");
    }

    [Fact]
    public async Task Checkpoint_CheckThrows_StillMarksFiredTasks()
    {
        var task = MakeRecurringTask("t2");
        var store = new Mock<ICronTaskStore>();

        var getAllCallCount = 0;
        store.Setup(s => s.GetAllTasksAsync(It.IsAny<CancellationToken>()))
             .Returns(() =>
             {
                 getAllCallCount++;
                 if (getAllCallCount == 1)
                 {
                     return Task.FromResult<IReadOnlyList<CronTask>>(new List<CronTask> { task });
                 }
                 throw new InvalidOperationException("simulated check failure");
             });

        var markCalled = false;
        store.Setup(s => s.MarkTasksFiredAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
             .Callback(() => markCalled = true)
             .Returns(Task.CompletedTask);

        var options = new CronSchedulerOptions
        {
            OnFire = _ => Task.CompletedTask,
            CheckIntervalMs = 30
        };

        var fakeTime = new FakeTimeProvider();
        var clock = new Mock<IClockService>();
        clock.SetupGet(c => c.TimeProvider).Returns(fakeTime);
        clock.Setup(c => c.GetUtcNowOffset()).Returns(() => fakeTime.GetUtcNow());

        await using var scheduler = new CronScheduler(options, store.Object, clock.Object);
        await scheduler.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (getAllCallCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await Task.Delay(100);
        await scheduler.StopAsync();

        Assert.True(markCalled, "即使后续 Check 抛异常，首次触发的任务也应被 MarkTasksFiredAsync 标记");
    }
}
