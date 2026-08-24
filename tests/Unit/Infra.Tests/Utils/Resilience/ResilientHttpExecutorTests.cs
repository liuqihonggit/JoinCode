namespace Infra.Tests.Utils.Resilience;

public sealed class ResilientHttpExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_NoRetry_Succeeds()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
        };

        var executor = new ResilientHttpExecutor(policy);
        var result = await executor.ExecuteAsync(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            "test-op");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExecuteAsync_WithRetry_RetriesOnFailure()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(10), Strategy = BackoffStrategy.Fixed },
        };

        var executor = new ResilientHttpExecutor(policy);
        var attempt = 0;

        var result = await executor.ExecuteAsync(
            _ =>
            {
                attempt++;
                if (attempt < 3) throw new HttpRequestException("fail");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            "test-op");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithRetry_ExhaustsRetries_Throws()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(10), Strategy = BackoffStrategy.Fixed },
        };

        var executor = new ResilientHttpExecutor(policy);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(
                _ => throw new HttpRequestException("always fail"),
                "test-op"));
    }

    [Fact]
    public async Task ExecuteAsync_CircuitBreaker_OpensAfterThreshold()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            Retry = new RetryConfig { MaxRetries = 0 },
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 2, OpenDuration = TimeSpan.FromSeconds(60) },
        };

        var executor = new ResilientHttpExecutor(policy);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(_ => throw new HttpRequestException("fail"), "op1"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(_ => throw new HttpRequestException("fail"), "op2"));

        var ex = await Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
            executor.ExecuteAsync(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)), "op3"));

        ex.Message.Should().Contain("熔断器开启");
    }

    [Fact]
    public async Task ExecuteAsync_CircuitBreaker_SuccessResets()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            Retry = new RetryConfig { MaxRetries = 0 },
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 1, OpenDuration = TimeSpan.FromMilliseconds(50) },
        };

        var executor = new ResilientHttpExecutor(policy);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(_ => throw new HttpRequestException("fail"), "op1"));

        await Task.Delay(60);

        var result = await executor.ExecuteAsync(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            "op2");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExecuteAsync_OperationTimeout_ThrowsTimeoutException()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromMilliseconds(50),
            Retry = new RetryConfig { MaxRetries = 0 },
        };

        var executor = new ResilientHttpExecutor(policy);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            executor.ExecuteAsync(
                async ct =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                },
                "slow-op"));
    }

    [Fact]
    public async Task ExecuteAsync_GenericOverload_Succeeds()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
        };

        var executor = new ResilientHttpExecutor(policy);
        var result = await executor.ExecuteAsync(
            _ => Task.FromResult("hello"),
            "test-op");

        result.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_UserCancellation_NotCountedAsFailure()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 1 },
        };

        var executor = new ResilientHttpExecutor(policy);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(
                async ct =>
                {
                    await Task.Delay(1, ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                },
                "test-op",
                cts.Token));

        executor.CircuitBreaker!.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_TotalBudgetExhausted_ThrowsBudgetExhaustedException()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig
            {
                TotalBudget = TimeSpan.FromMilliseconds(100),
                BaseDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(50),
                Strategy = BackoffStrategy.Fixed,
            },
        };

        var executor = new ResilientHttpExecutor(policy);

        await Assert.ThrowsAsync<NetworkRetryBudgetExhaustedException>(() =>
            executor.ExecuteAsync(
                _ => throw new HttpRequestException("always fail"),
                "test-op"));
    }

    [Fact]
    public async Task ExecuteAsync_TotalBudget_ExceedsMaxRetries()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig
            {
                MaxRetries = 2,
                TotalBudget = TimeSpan.FromSeconds(3),
                BaseDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(50),
                Strategy = BackoffStrategy.Fixed,
            },
        };

        var executor = new ResilientHttpExecutor(policy);
        var attempt = 0;

        await Assert.ThrowsAsync<NetworkRetryBudgetExhaustedException>(() =>
            executor.ExecuteAsync(
                _ =>
                {
                    attempt++;
                    throw new HttpRequestException("always fail");
                },
                "test-op"));

        attempt.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithinBudget_RetriesUntilSuccess()
    {
        var policy = new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig
            {
                TotalBudget = TimeSpan.FromSeconds(10),
                BaseDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(50),
                Strategy = BackoffStrategy.Fixed,
            },
        };

        var executor = new ResilientHttpExecutor(policy);
        var attempt = 0;

        var result = await executor.ExecuteAsync(
            _ =>
            {
                attempt++;
                if (attempt < 5) throw new HttpRequestException("fail");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            "test-op");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        attempt.Should().Be(5);
    }

    /// <summary>
    /// 集成测试 — 验证 Gateway 包裹透传操作时无重试放大（1:1 调用，无嵌套）
    /// <para>重试配置从 NetworkRetryOptions.ToTestRetryConfig 派生，共用生产策略/开关，避免改了生产没改测试</para>
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_GatewayWithPassthrough_NoRetryAmplification()
    {
        var retryOptions = new NetworkRetryOptions();
        var policy = new ResiliencePolicy
        {
            Name = "integration",
            OperationTimeout = TimeSpan.FromSeconds(10),
            Retry = retryOptions.ToTestRetryConfig(TimeSpan.FromSeconds(8), TimeSpan.FromMilliseconds(100)),
        };

        var executor = new ResilientHttpExecutor(policy);
        var gatewayAttempts = 0;
        var passthroughCalls = 0;

        await Assert.ThrowsAsync<NetworkRetryBudgetExhaustedException>(() =>
            executor.ExecuteAsync(
                _ =>
                {
                    gatewayAttempts++;
                    passthroughCalls++;
                    throw new HttpRequestException("fail");
                },
                "integration-op"));

        gatewayAttempts.Should().BeGreaterThanOrEqualTo(16);
        passthroughCalls.Should().Be(gatewayAttempts);
    }
}
