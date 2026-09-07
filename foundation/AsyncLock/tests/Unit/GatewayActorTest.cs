namespace Core.Utils;

/// <summary>
/// GatewayActor 单元测试 — 验证限流、重试、熔断。
/// </summary>
public class GatewayActorTest
{
    [Fact]
    public void GatewayOptions_LlmGateway_HasCorrectValues()
    {
        GatewayOptions.LlmGateway.MaxConcurrency.Should().Be(10);
        GatewayOptions.LlmGateway.MaxRetries.Should().Be(3);
        GatewayOptions.LlmGateway.CircuitBreakerThreshold.Should().Be(5);
    }

    [Fact]
    public void GatewayOptions_EffectiveDelays_HaveDefaults()
    {
        var opts = new GatewayOptions();
        opts.EffectiveRetryDelay.Should().Be(TimeSpan.FromSeconds(1));
        opts.EffectiveRecoveryDelay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task CallAsync_Success_ReturnsResponse()
    {
        var gateway = new GatewayActor<string, string>(
            (req, ct) => Task.FromResult($"echo:{req}"),
            new GatewayOptions(MaxConcurrency: 1, MaxRetries: 0, CircuitBreakerThreshold: 0));
        await using var _ = gateway;

        var result = await gateway.CallAsync("hello");
        result.Should().Be("echo:hello");
    }

    [Fact]
    public async Task CallAsync_RetriesOnFailure_ThenSucceeds()
    {
        var callCount = 0;
        var gateway = new GatewayActor<string, string>(
            (req, ct) =>
            {
                callCount++;
                if (callCount < 3) throw new InvalidOperationException("fail");
                return Task.FromResult("ok");
            },
            new GatewayOptions(MaxConcurrency: 1, MaxRetries: 3, RetryBaseDelay: TimeSpan.FromMilliseconds(10), CircuitBreakerThreshold: 0));
        await using var _ = gateway;

        var result = await gateway.CallAsync("test");
        result.Should().Be("ok");
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task CallAsync_AllRetriesFail_ThrowsException()
    {
        var gateway = new GatewayActor<string, string>(
            (req, ct) => throw new InvalidOperationException("always fail"),
            new GatewayOptions(MaxConcurrency: 1, MaxRetries: 2, RetryBaseDelay: TimeSpan.FromMilliseconds(10), CircuitBreakerThreshold: 0));
        await using var _ = gateway;

        var act = async () => await gateway.CallAsync("test");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterThresholdFailures()
    {
        var gateway = new GatewayActor<string, string>(
            (req, ct) => throw new InvalidOperationException("fail"),
            new GatewayOptions(MaxConcurrency: 1, MaxRetries: 0, CircuitBreakerThreshold: 3, CircuitBreakerRecoveryDelay: TimeSpan.FromSeconds(60)));
        await using var _ = gateway;

        for (var i = 0; i < 3; i++)
        {
            await SwallowAsync(() => gateway.CallAsync("test"));
        }

        gateway.BreakerState.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task CircuitBreaker_RejectsCallWhenOpen()
    {
        var gateway = new GatewayActor<string, string>(
            (req, ct) => throw new InvalidOperationException("fail"),
            new GatewayOptions(MaxConcurrency: 1, MaxRetries: 0, CircuitBreakerThreshold: 1, CircuitBreakerRecoveryDelay: TimeSpan.FromSeconds(60)));
        await using var _ = gateway;

        await SwallowAsync(() => gateway.CallAsync("test"));

        var act = async () => await gateway.CallAsync("test");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*熔断*");
    }

    [Fact]
    public async Task CircuitBreaker_ResetsOnSuccess()
    {
        var callCount = 0;
        var gateway = new GatewayActor<string, string>(
            (req, ct) =>
            {
                callCount++;
                if (callCount <= 2) throw new InvalidOperationException("fail");
                return Task.FromResult("ok");
            },
            new GatewayOptions(MaxConcurrency: 1, MaxRetries: 0, CircuitBreakerThreshold: 5, RetryBaseDelay: TimeSpan.FromMilliseconds(10)));
        await using var _ = gateway;

        await SwallowAsync(() => gateway.CallAsync("test"));
        await SwallowAsync(() => gateway.CallAsync("test"));
        var result = await gateway.CallAsync("test");

        gateway.BreakerState.Should().Be(CircuitBreakerState.Closed);
        gateway.ConsecutiveFailures.Should().Be(0);
    }

    private static async Task SwallowAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception) { Console.WriteLine("[Test] 预期失败已吞掉"); }
    }
}
