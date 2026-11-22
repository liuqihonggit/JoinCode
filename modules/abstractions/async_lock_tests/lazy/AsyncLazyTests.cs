namespace LazyDiagnosis.Tests;

/// <summary>AsyncLazy 单飞语义回归 — 并发只执行一次工厂、异常共享、释放后拒绝访问。</summary>
public sealed class AsyncLazyTests {
    [Fact]
    public async Task ConcurrentCallers_ExecuteFactoryOnce() {
        var invocationCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lazy = new AsyncLazy<int>(async () => {
            Interlocked.Increment(ref invocationCount);
            await gate.Task;
            return 42;
        });

        var callers = Enumerable.Range(0, 64)
            .Select(_ => lazy.GetValueAsync().AsTask())
            .ToArray();

        gate.SetResult();
        var values = await Task.WhenAll(callers);

        invocationCount.Should().Be(1);
        values.Should().OnlyContain(v => v == 42);
        lazy.IsValueCreated.Should().BeTrue();
    }

    [Fact]
    public async Task FactoryFailure_IsCachedForLaterCallers() {
        var invocationCount = 0;
        var lazy = new AsyncLazy<int>((Func<int>)(() => {
            Interlocked.Increment(ref invocationCount);
            throw new InvalidOperationException("boom");
        }));

        var first = await Record.ExceptionAsync(() => lazy.GetValueAsync().AsTask());
        var second = await Record.ExceptionAsync(() => lazy.GetValueAsync().AsTask());

        first.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
        second.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
        invocationCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_RejectsSubsequentAccess() {
        var lazy = new AsyncLazy<int>(() => 7);
        await lazy.DisposeAsync();

        await FluentActions.Awaiting(() => lazy.GetValueAsync().AsTask())
            .Should().ThrowAsync<ObjectDisposedException>();
    }
}
