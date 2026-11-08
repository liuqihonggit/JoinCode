namespace Core.Utils;

public class FireAndForgetTaskTests {
    [Fact]
    public async Task Run_WithResult_CompletesSuccessfully() {
        await using var fft = FireAndForgetTask<int>.Run(ct => System.Threading.Tasks.Task.FromResult(42));

        var result = await fft.Task;
        result.Should().Be(42);
        fft.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Run_WithoutResult_CompletesSuccessfully() {
        await using var fft = FireAndForgetTask<Unit>.Run(async ct => {
            await System.Threading.Tasks.Task.Delay(10, ct);
        });

        await fft.Task;
        fft.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Run_FactoryReceivesCancellationToken() {
        await using var fft = FireAndForgetTask<Unit>.Run(async ct => {
            ct.Should().NotBe(default);
            await System.Threading.Tasks.Task.Delay(10, ct);
        });
    }

    [Fact]
    public async Task Release_WaitsForCompletion() {
        var tcs = new TaskCompletionSource<int>();
        await using var fft = FireAndForgetTask<int>.Run(ct => tcs.Task);

        fft.IsCompleted.Should().BeFalse();

        tcs.SetResult(99);
        await fft.DisposeAsync();
        fft.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Release_CancelsTokenBeforeWaiting() {
        var tcs = new TaskCompletionSource<int>();
        CancellationToken observedToken = default;
        await using var fft = FireAndForgetTask<int>.Run(async ct => {
            observedToken = ct;
            await tcs.Task;
            return 1;
        });

        await System.Threading.Tasks.Task.Delay(50);
        observedToken.CanBeCanceled.Should().BeTrue();

        var releaseTask = fft.DisposeAsync().AsTask();
        await System.Threading.Tasks.Task.Delay(50);
        observedToken.IsCancellationRequested.Should().BeTrue();

        tcs.SetCanceled();
        await releaseTask;
    }

    [Fact]
    public async Task Release_SwallowsException() {
        await using var fft = FireAndForgetTask<int>.Run(async ct => {
            await System.Threading.Tasks.Task.Delay(10, ct);
            throw new InvalidOperationException("boom");
        });

        var act = async () => await fft.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Release_WithGracePeriod_CompletesWhenTaskFinishesInTime() {
        var tcs = new TaskCompletionSource<int>();
        await using var fft = FireAndForgetTask<int>.Run(ct => tcs.Task);

        tcs.SetResult(1);
        await fft.DisposeAsync(TimeSpan.FromSeconds(5));
        fft.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Release_WithGracePeriod_DoesNotWaitWhenTimeout() {
        var tcs = new TaskCompletionSource<int>();
        await using var fft = FireAndForgetTask<int>.Run(ct => tcs.Task);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await fft.DisposeAsync(TimeSpan.FromMilliseconds(100));
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Release_WithZeroGracePeriod_ReturnsImmediately() {
        var tcs = new TaskCompletionSource<int>();
        await using var fft = FireAndForgetTask<int>.Run(ct => tcs.Task);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await fft.DisposeAsync(TimeSpan.Zero);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Release_Idempotent() {
        await using var fft = FireAndForgetTask<int>.Run(ct => System.Threading.Tasks.Task.FromResult(1));

        await fft.DisposeAsync();
        await fft.DisposeAsync();
    }

    [Fact]
    public async Task Task_Property_PropagatesException() {
        await using var fft = FireAndForgetTask<int>.Run(async ct => {
            await System.Threading.Tasks.Task.Delay(10, ct);
            throw new InvalidOperationException("boom");
        });

        var act = async () => await fft.Task;
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Run_Name_StoredForDiagnostics() {
        await using var fft = FireAndForgetTask<int>.Run(ct => System.Threading.Tasks.Task.FromResult(1), "test-task");

        fft.Name.Should().Be("test-task");
    }
}
