namespace Infra.Tests.Subprocess;

public sealed class ResilientChannelTests
{
    [Fact]
    public async Task ExecuteAsync_Success_ReturnsResult()
    {
        using var channel = new ResilientChannel("test", null, TimeSpan.FromSeconds(5));

        var result = await channel.ExecuteAsync(_ => Task.FromResult(42));

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_Void_Success()
    {
        using var channel = new ResilientChannel("test", null, TimeSpan.FromSeconds(5));
        var executed = false;

        await channel.ExecuteAsync(_ => { executed = true; return Task.CompletedTask; });

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ThrowsTimeoutException()
    {
        using var channel = new ResilientChannel("test", null, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            channel.ExecuteAsync(async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return 0;
            }));
    }

    [Fact]
    public async Task ExecuteAsync_CircuitBreakerOpen_ThrowsCircuitBreakerOpenException()
    {
        var cb = new UnifiedCircuitBreaker("test-cb", failureThreshold: 1, openDuration: TimeSpan.FromMinutes(1));
        using var channel = new ResilientChannel("test", cb, TimeSpan.FromSeconds(5));

        cb.RecordFailure();

        await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
            channel.ExecuteAsync(_ => Task.FromResult(1)));
    }

    [Fact]
    public async Task ExecuteAsync_Success_RecordsCircuitBreakerSuccess()
    {
        var cb = new UnifiedCircuitBreaker("test-cb", failureThreshold: 3, openDuration: TimeSpan.FromMinutes(1));
        using var channel = new ResilientChannel("test", cb, TimeSpan.FromSeconds(5));

        cb.RecordFailure();
        cb.RecordFailure();

        await channel.ExecuteAsync(_ => Task.FromResult(1));

        cb.ConsecutiveFailures.Should().Be(0);
        cb.TotalSuccesses.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Failure_RecordsCircuitBreakerFailure()
    {
        var cb = new UnifiedCircuitBreaker("test-cb", failureThreshold: 5, openDuration: TimeSpan.FromMinutes(1));
        using var channel = new ResilientChannel("test", cb, TimeSpan.FromSeconds(5));

        var ex = await Record.ExceptionAsync(() =>
            channel.ExecuteAsync<int>(_ => throw new InvalidOperationException("boom")));

        ex.Should().BeOfType<InvalidOperationException>();

        cb.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Propagates()
    {
        using var channel = new ResilientChannel("test", null, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Record.ExceptionAsync(() =>
            channel.ExecuteAsync(_ => Task.FromResult(1), cts.Token));

        ex.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_Concurrent_SerializedByLock()
    {
        using var channel = new ResilientChannel("test", null, TimeSpan.FromSeconds(5));
        var executionOrder = new List<int>();
        using var gate = new SemaphoreSlim(0, 1);

        var task1 = Task.Run(async () => await channel.ExecuteAsync(async _ =>
        {
            executionOrder.Add(1);
            await gate.WaitAsync();
            return 0;
        }));

        await Task.Delay(50);

        var task2 = Task.Run(async () => await channel.ExecuteAsync(_ =>
        {
            executionOrder.Add(2);
            return Task.FromResult(0);
        }));

        await Task.Delay(50);
        executionOrder.Should().Contain(1);
        executionOrder.Should().NotContain(2);

        gate.Release();
        await Task.WhenAll(task1, task2);

        executionOrder.Should().Equal(1, 2);
    }
}
