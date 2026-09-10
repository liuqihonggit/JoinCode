namespace Core.Tests.Plugins;

public sealed class PluginContextBackgroundTaskTests
{
    private static PluginContext CreateContext(
        out CancellationTokenSource shutdownCts,
        out ServiceCollection services)
    {
        shutdownCts = new CancellationTokenSource();
        services = new ServiceCollection();
        return new PluginContext("test-plugin", services, shutdownCts.Token);
    }

    private static void InvokeUndoChain(PluginContext ctx)
    {
        foreach (var undo in ctx.GetUndoChain().Reverse())
        {
            undo.Invoke();
        }
    }

    [Fact]
    public async Task RunBackgroundTask_TaskExecutes()
    {
        var ctx = CreateContext(out var cts, out _);
        var executed = new TaskCompletionSource<bool>();

        _ = ctx.RunBackgroundTask(_ =>
        {
            executed.SetResult(true);
            return Task.CompletedTask;
        });

        var result = await executed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        cts.Cancel();
        Assert.True(result);
    }

    [Fact]
    public async Task RunBackgroundTask_UnloadWaitsForExit()
    {
        var ctx = CreateContext(out _, out _);
        var taskStarted = new TaskCompletionSource();
        var taskExiting = new TaskCompletionSource();

        _ = ctx.RunBackgroundTask(async _ =>
        {
            taskStarted.SetResult();
            await taskExiting.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }, TimeSpan.FromSeconds(5));

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        var beforeExit = DateTime.UtcNow;
        taskExiting.SetResult();

        InvokeUndoChain(ctx);

        var waitedTime = DateTime.UtcNow - beforeExit;
        Assert.True(waitedTime < TimeSpan.FromSeconds(2),
            $"撤销应在任务退出后立即返回,实际等待 {waitedTime.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task RunBackgroundTask_UnloadTimeout_ThrowsTimeoutException()
    {
        var ctx = CreateContext(out _, out _);
        var taskStarted = new TaskCompletionSource();

        _ = ctx.RunBackgroundTask(async _ =>
        {
            taskStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(10));
        }, TimeSpan.FromMilliseconds(100));

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        var ex = Assert.Throws<TimeoutException>(() => InvokeUndoChain(ctx));
        Assert.Contains("后台任务", ex.Message);
    }

    [Fact]
    public async Task RunBackgroundTask_ActionOverload_Works()
    {
        var ctx = CreateContext(out var cts, out _);
        var executed = new TaskCompletionSource<bool>();

        _ = ctx.RunBackgroundTask(_ => executed.SetResult(true));

        var result = await executed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        cts.Cancel();
        Assert.True(result);
    }

    [Fact]
    public async Task RunBackgroundTask_ShutdownToken_PassedToWork()
    {
        var ctx = CreateContext(out var cts, out _);
        CancellationToken receivedToken = default;
        var tokenCaptured = new TaskCompletionSource();

        _ = ctx.RunBackgroundTask(token =>
        {
            receivedToken = token;
            tokenCaptured.SetResult();
            return Task.CompletedTask;
        });

        await tokenCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        cts.Cancel();
        Assert.True(receivedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task RunBackgroundTask_ZeroWait_DoesNotBlock()
    {
        var ctx = CreateContext(out _, out _);
        var taskStarted = new TaskCompletionSource();

        _ = ctx.RunBackgroundTask(async _ =>
        {
            taskStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(5));
        }, TimeSpan.Zero);

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        InvokeUndoChain(ctx);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1),
            $"ZeroWait 应立即返回,实际 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task RunBackgroundTask_NullWork_Throws()
    {
        var ctx = CreateContext(out _, out _);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Task.Run(() => ctx.RunBackgroundTask((Func<CancellationToken, Task>)null!)));
    }
}
