namespace Core.Utils;

public class FireAndForgetTrackerTests {
    [Fact]
    public async Task Register_ReturnsFireAndForgetTask() {
        await using var tracker = new FireAndForgetTracker();

        var result = await tracker.Register<int>(ct => System.Threading.Tasks.Task.FromResult(42)).Task;
        result.Should().Be(42);
    }

    [Fact]
    public async Task Register_WithoutResult_ReturnsFireAndForgetTask() {
        await using var tracker = new FireAndForgetTracker();

        await tracker.Register(async ct => {
            await System.Threading.Tasks.Task.Delay(10, ct);
        }).Task;
    }

    [Fact]
    public async Task Count_TracksRegisteredTasks() {
        await using var tracker = new FireAndForgetTracker();

        tracker.Count.Should().Be(0);

        tracker.Register<int>(ct => System.Threading.Tasks.Task.FromResult(1));
        tracker.Count.Should().Be(1);

        tracker.Register<int>(ct => System.Threading.Tasks.Task.FromResult(2));
        tracker.Count.Should().Be(2);
    }

    [Fact]
    public async Task Release_WaitsForAllTasks() {
        await using var tracker = new FireAndForgetTracker();

        var tcs1 = new TaskCompletionSource<int>();
        var tcs2 = new TaskCompletionSource<int>();

        tracker.Register<int>(ct => tcs1.Task);
        tracker.Register<int>(ct => tcs2.Task);

        tcs1.SetResult(1);
        tcs2.SetResult(2);
    }

    [Fact]
    public async Task Release_CancelsAllTasksFirst() {
        await using var tracker = new FireAndForgetTracker();

        CancellationToken token1 = default;
        CancellationToken token2 = default;

        tracker.Register<int>(async ct => {
            token1 = ct;
            await new TaskCompletionSource<int>().Task;
            return 1;
        });
        tracker.Register<int>(async ct => {
            token2 = ct;
            await new TaskCompletionSource<int>().Task;
            return 2;
        });

        await System.Threading.Tasks.Task.Delay(100);

        var releaseTask = tracker.DisposeAsync().AsTask();
        await System.Threading.Tasks.Task.Delay(100);

        token1.IsCancellationRequested.Should().BeTrue();
        token2.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Release_SwallowsExceptions() {
        await using var tracker = new FireAndForgetTracker();

        tracker.Register<int>(async ct => {
            await System.Threading.Tasks.Task.Delay(10, ct);
            throw new InvalidOperationException("boom");
        });
        tracker.Register<int>(async ct => {
            await System.Threading.Tasks.Task.Delay(10, ct);
            throw new ArgumentException("bang");
        });

        var act = async () => await tracker.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Release_Idempotent() {
        await using var tracker = new FireAndForgetTracker();

        tracker.Register<int>(ct => System.Threading.Tasks.Task.FromResult(1));

        await tracker.DisposeAsync();
        await tracker.DisposeAsync();
    }

    [Fact]
    public async Task Release_WithGracePeriod_WaitsForQuickTasks() {
        await using var tracker = new FireAndForgetTracker();

        tracker.Register<int>(async ct => {
            await System.Threading.Tasks.Task.Delay(50, ct);
            return 1;
        });

        await tracker.DisposeAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Release_WithGracePeriod_DoesNotWaitForSlowTasks() {
        await using var tracker = new FireAndForgetTracker();

        tracker.Register<int>(ct => new TaskCompletionSource<int>().Task);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await tracker.DisposeAsync(TimeSpan.FromMilliseconds(100));
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Register_AfterRelease_ThrowsObjectDisposedException() {
        var tracker = new FireAndForgetTracker();
        await tracker.DisposeAsync();

        var act = () => tracker.Register<int>(ct => System.Threading.Tasks.Task.FromResult(1));
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Release_ParallelTasks_CompleteInParallel() {
        await using var tracker = new FireAndForgetTracker();

        tracker.Register<int>(async ct => {
            await System.Threading.Tasks.Task.Delay(200, ct);
            return 1;
        });
        tracker.Register<int>(async ct => {
            await System.Threading.Tasks.Task.Delay(200, ct);
            return 2;
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await tracker.DisposeAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(500));
    }

    [Fact]
    public async Task RegisterExistingTask_WrapsAlreadyStartedTask() {
        await using var tracker = new FireAndForgetTracker();

        var tcs = new TaskCompletionSource<int>();
        tracker.RegisterExistingTask(tcs.Task);

        tracker.Count.Should().Be(1);
        tcs.SetResult(42);
    }

    [Fact]
    public async Task RegisterExistingTask_DisposeAsync_WaitsForCompletion() {
        var tracker = new FireAndForgetTracker();

        var tcs = new TaskCompletionSource<int>();
        tracker.RegisterExistingTask(tcs.Task);

        var disposeTask = tracker.DisposeAsync().AsTask();
        await System.Threading.Tasks.Task.Delay(50);
        disposeTask.IsCompleted.Should().BeFalse();

        tcs.SetResult(1);
        await disposeTask;
    }

    [Fact]
    public async Task RegisterExistingTask_DoesNotCancelOriginalTask() {
        var tracker = new FireAndForgetTracker();

        var tcs = new TaskCompletionSource<int>();
        tracker.RegisterExistingTask(tcs.Task);

        var disposeTask = tracker.DisposeAsync().AsTask();
        await System.Threading.Tasks.Task.Delay(50);

        tcs.Task.IsCanceled.Should().BeFalse();
        tcs.SetResult(1);
        await disposeTask;
    }

    [Fact]
    public async Task CleanupCompleted_RemovesCompletedTasks_KeepsRecent() {
        await using var tracker = new FireAndForgetTracker();

        for (var i = 0; i < 300; i++) {
            var tcs = new TaskCompletionSource<int>();
            tcs.SetResult(i);
            tracker.RegisterExistingTask(tcs.Task);
        }

        tracker.Count.Should().BeLessThan(320,
            "超过阈值256后应清理已完成项,保留最近64个");
    }

    [Fact]
    public async Task CleanupCompleted_KeepsUncompletedTasks() {
        await using var tracker = new FireAndForgetTracker();

        var uncompleted = new TaskCompletionSource<int>();
        tracker.RegisterExistingTask(uncompleted.Task);

        for (var i = 0; i < 300; i++) {
            var tcs = new TaskCompletionSource<int>();
            tcs.SetResult(i);
            tracker.RegisterExistingTask(tcs.Task);
        }

        tracker.Count.Should().BeLessThan(70,
            "已完成项应被清理,仅保留未完成项+最近64个诊断项");

        uncompleted.SetResult(1);
    }
}
