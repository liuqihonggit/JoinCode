
namespace Core.Tests.Scheduling;

public class ExecutionContextTests
{
    [Fact]
    public async Task AddRunningTaskAsync_And_GetRunningTasksSnapshotAsync_ShouldWork()
    {
        await using var context = new Core.Scheduling.ExecutionContext(new ExecutionOptions { MaxConcurrentTasks = 2 }, CancellationToken.None);

        var t1 = Task.CompletedTask;
        var t2 = Task.Delay(1);

        await context.AddRunningTaskAsync(t1);
        await context.AddRunningTaskAsync(t2);

        var snapshot = await context.GetRunningTasksSnapshotAsync();
        snapshot.Should().HaveCount(2);
    }

    [Fact]
    public async Task CleanupCompletedTasksAsync_ShouldRemoveCompletedTasks()
    {
        await using var context = new Core.Scheduling.ExecutionContext(new ExecutionOptions { MaxConcurrentTasks = 2 }, CancellationToken.None);

        var completed = Task.CompletedTask;
        var running = Task.Delay(TimeSpan.FromMilliseconds(200));

        await context.AddRunningTaskAsync(completed);
        await context.AddRunningTaskAsync(running);

        await context.CleanupCompletedTasksAsync();

        var count = await context.GetRunningTaskCountAsync();
        count.Should().Be(1);

        await running;
    }

    [Fact]
    public async Task TryMarkCompleted_ShouldReturnTrueOnlyOnce()
    {
        await using var context = new Core.Scheduling.ExecutionContext(new ExecutionOptions { MaxConcurrentTasks = 2 }, CancellationToken.None);

        context.TryMarkCompleted("id-1").Should().BeTrue();
        context.TryMarkCompleted("id-1").Should().BeFalse();
        context.IsCompleted("id-1").Should().BeTrue();
    }

    [Fact]
    public async Task GetCompletedTaskIds_ShouldReturnSnapshot()
    {
        await using var context = new Core.Scheduling.ExecutionContext(new ExecutionOptions { MaxConcurrentTasks = 1 }, CancellationToken.None);

        context.TryMarkCompleted("a");
        context.TryMarkCompleted("b");

        context.GetCompletedTaskIds().Should().Contain("a").And.Contain("b");
    }

    [Fact]
    public async Task Options_And_CancellationToken_ShouldBeSet()
    {
        var options = new ExecutionOptions { MaxConcurrentTasks = 5 };
        using var cts = new CancellationTokenSource();

        await using var context = new Core.Scheduling.ExecutionContext(options, cts.Token);

        context.Options.Should().Be(options);
        context.CancellationToken.Should().Be(cts.Token);
    }
}
