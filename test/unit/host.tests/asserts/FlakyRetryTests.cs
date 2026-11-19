namespace Host.Tests.Asserts;

/// <summary>FlakyRetry 外侧重试模板 — 覆盖瞬态失败重试/条件满足返回/耗尽聚合报错（TASK101 B3）。</summary>
public sealed class FlakyRetryTests {
    [Fact]
    public async Task RunUntilAsync_TransientFailuresThenSuccess_ReturnsAfterRetries() {
        var calls = 0;

        await FlakyRetry.RunUntilAsync(
            () => Task.FromResult(++calls >= 3),
            "条件应在第 3 次满足",
            maxAttempts: 5);

        calls.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_ThrowsTwiceThenSucceeds_Returns() {
        var calls = 0;

        await FlakyRetry.RunAsync(
            () => ++calls < 3
                ? throw new InvalidOperationException("transient")
                : Task.CompletedTask,
            maxAttempts: 5);

        calls.Should().Be(3);
    }

    [Fact]
    public async Task RunUntilAsync_AllAttemptsExhausted_ThrowsWithAggregatedAttempts() {
        var calls = 0;

        var act = async () => await FlakyRetry.RunUntilAsync(
            () => {
                calls++;
                throw new TimeoutException($"timeout-{calls}");
            },
            "永不满足的条件",
            maxAttempts: 4);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("永不满足的条件").And.Contain("4 次尝试全部失败")
            .And.Contain("attempt 1: TimeoutException: timeout-1")
            .And.Contain("attempt 4: TimeoutException: timeout-4");
        ex.Which.InnerException.Should().BeOfType<TimeoutException>();
        calls.Should().Be(4);
    }

    [Fact]
    public async Task RunUntilAsync_ConditionNeverTrue_ThrowsWithoutInnerException() {
        var calls = 0;

        var act = async () => await FlakyRetry.RunUntilAsync(
            () => Task.FromResult(++calls > 100),
            "条件永假",
            maxAttempts: 2);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("attempt 1: 条件未满足").And.Contain("attempt 2: 条件未满足");
        ex.Which.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task RunUntilAsync_Cancellation_PropagatesImmediately() {
        var calls = 0;

        var act = async () => await FlakyRetry.RunUntilAsync(
            () => {
                calls++;
                throw new OperationCanceledException("cancelled");
            },
            "取消不应被重试",
            maxAttempts: 5);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }
}
