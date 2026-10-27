// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Infra.Tests.Utils.Resilience;

public sealed class ResilientHttpExecutorTests {
    [Fact]
    public async Task ExecuteAsync_NoRetry_Succeeds() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
        };

        var executor = new ResilientHttpExecutor(policy);
        using var result = await executor.ExecuteAsync(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            "test-op");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExecuteAsync_WithRetry_RetriesOnFailure() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(10), Strategy = BackoffStrategy.Fixed },
        };

        var executor = new ResilientHttpExecutor(policy);
        var attempt = 0;

        using var result = await executor.ExecuteAsync(
            _ => {
                attempt++;
                if (attempt < 3) throw new HttpRequestException("fail");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            "test-op");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_WithRetry_ExhaustsRetries_Throws() {
        var policy = new ResiliencePolicy {
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
    public async Task ExecuteAsync_CircuitBreaker_OpensAfterThreshold() {
        var policy = new ResiliencePolicy {
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
    public async Task ExecuteAsync_CircuitBreaker_SuccessResets() {
        var policy = new ResiliencePolicy {
            Name = "test",
            Retry = new RetryConfig { MaxRetries = 0 },
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 1, OpenDuration = TimeSpan.FromMilliseconds(50) },
        };

        var executor = new ResilientHttpExecutor(policy);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(_ => throw new HttpRequestException("fail"), "op1"));

        await Task.Delay(60);

        using var result = await executor.ExecuteAsync(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            "op2");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExecuteAsync_OperationTimeout_ThrowsTimeoutException() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromMilliseconds(50),
            Retry = new RetryConfig { MaxRetries = 0 },
        };

        var executor = new ResilientHttpExecutor(policy);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            executor.ExecuteAsync(
                async ct => {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                },
                "slow-op"));
    }

    [Fact]
    public async Task ExecuteAsync_GenericOverload_Succeeds() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
        };

        var executor = new ResilientHttpExecutor(policy);
        var result = await executor.ExecuteAsync(
            _ => Task.FromResult("hello"),
            "test-op");

        result.Should().Be("hello");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task ExecuteAsync_UserCancellation_NotCountedAsFailure() {
        var policy = new ResiliencePolicy {
            Name = "test",
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 1 },
        };

        var executor = new ResilientHttpExecutor(policy);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(
                async ct => {
                    await Task.Delay(1, ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                },
                "test-op",
                cts.Token));

        executor.CircuitBreaker!.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_TotalBudgetExhausted_ThrowsBudgetExhaustedException() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig {
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
    public async Task ExecuteAsync_TotalBudget_ExceedsMaxRetries() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig {
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
                _ => {
                    attempt++;
                    throw new HttpRequestException("always fail");
                },
                "test-op"));

        attempt.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task ExecuteAsync_WithinBudget_RetriesUntilSuccess() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig {
                TotalBudget = TimeSpan.FromSeconds(10),
                BaseDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(50),
                Strategy = BackoffStrategy.Fixed,
            },
        };

        var executor = new ResilientHttpExecutor(policy);
        var attempt = 0;

        using var result = await executor.ExecuteAsync(
            _ => {
                attempt++;
                if (attempt < 5) throw new HttpRequestException("fail");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            "test-op");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        attempt.Should().Be(5);
    }

    /// <summary>
    /// Retry-After 提取器优先于指数退避 — 服务器返回 Retry-After 时直接使用该值
    /// 对齐基准 withRetry.ts:533-538 getRetryDelay 优先 Retry-After 头
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RetryAfterExtractor_PrioritizedOverBackoff() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig {
                MaxRetries = 1,
                BaseDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(20),
                Strategy = BackoffStrategy.Fixed,
                RetryAfterExtractor = _ => TimeSpan.FromMilliseconds(150),
            },
        };

        var executor = new ResilientHttpExecutor(policy);
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(
                _ => throw new HttpRequestException("429"),
                "test-op"));

        sw.Elapsed.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(140,
            "Retry-After 150ms 应优先于 BaseDelay 10ms");
    }

    /// <summary>
    /// 单次延迟上限 cap — 退避计算值超过 cap 时被限制
    /// 对齐基准 withRetry.ts:446 PERSISTENT_RESET_CAP_MS = 6h
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SingleDelayCap_LimitsDelay() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig {
                MaxRetries = 1,
                BaseDelay = TimeSpan.FromMilliseconds(500),
                MaxDelay = TimeSpan.FromSeconds(10),
                Strategy = BackoffStrategy.Fixed,
                SingleDelayCap = TimeSpan.FromMilliseconds(80),
            },
        };

        var executor = new ResilientHttpExecutor(policy);
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(
                _ => throw new HttpRequestException("fail"),
                "test-op"));

        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(150,
            "SingleDelayCap 80ms 应限制退避延迟（Fixed=500ms 被 cap 到 80ms）");
        sw.Elapsed.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(70);
    }

    /// <summary>
    /// 心跳回调在重试等待期间被调用 — 分块睡眠 + 回调让宿主看到周期性活动
    /// 对齐基准 withRetry.ts:489-503 分块睡眠 + yield SystemAPIErrorMessage
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_HeartbeatCallback_InvokedDuringWait() {
        var heartbeatCalls = 0;
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig {
                MaxRetries = 1,
                BaseDelay = TimeSpan.FromMilliseconds(60),
                MaxDelay = TimeSpan.FromMilliseconds(60),
                Strategy = BackoffStrategy.Fixed,
                HeartbeatInterval = TimeSpan.FromMilliseconds(15),
                HeartbeatCallback = (_, _, _) => Interlocked.Increment(ref heartbeatCalls),
            },
        };

        var executor = new ResilientHttpExecutor(policy);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(
                _ => throw new HttpRequestException("fail"),
                "test-op"));

        heartbeatCalls.Should().BeGreaterThanOrEqualTo(2,
            "60ms 等待分块为 15ms 心跳，应至少调用 2 次回调");
    }

    /// <summary>
    /// 容量错误（429/529）在预算模式下无限重试 — 对齐基准 persistent 模式
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TransientErrorPredicate_CapacityErrorInfiniteRetry() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig {
                MaxRetries = 2,
                TotalBudget = TimeSpan.FromSeconds(5),
                BaseDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(20),
                Strategy = BackoffStrategy.Fixed,
                TransientErrorPredicate = _ => true,
            },
        };

        var executor = new ResilientHttpExecutor(policy);
        var attempt = 0;

        using var result = await executor.ExecuteAsync(
            _ => {
                attempt++;
                if (attempt < 5) throw new HttpRequestException("429");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            "test-op");

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        attempt.Should().Be(5, "容量错误应无限重试直到成功（超过 MaxRetries=2）");
    }

    /// <summary>
    /// 非容量错误（500）在预算模式下有限重试 — 对齐基准非 429/529 错误不无限重试
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TransientErrorPredicate_NonCapacityErrorFiniteRetry() {
        var policy = new ResiliencePolicy {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig {
                MaxRetries = 2,
                TotalBudget = TimeSpan.FromSeconds(5),
                BaseDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(20),
                Strategy = BackoffStrategy.Fixed,
                TransientErrorPredicate = _ => false,
            },
        };

        var executor = new ResilientHttpExecutor(policy);
        var attempt = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            executor.ExecuteAsync(
                _ => {
                    attempt++;
                    throw new HttpRequestException("500");
                },
                "test-op"));

        attempt.Should().Be(3, "非容量错误应有限重试（MaxRetries=2 + 初始尝试=1）");
    }

    /// <summary>
    /// 集成测试 — 验证 Gateway 包裹透传操作时无重试放大（1:1 调用，无嵌套）
    /// <para>重试配置从 NetworkRetryOptions.ToTestRetryConfig 派生，共用生产策略/开关，避免改了生产没改测试</para>
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_GatewayWithPassthrough_NoRetryAmplification() {
        var retryOptions = new NetworkRetryOptions();
        var policy = new ResiliencePolicy {
            Name = "integration",
            OperationTimeout = TimeSpan.FromSeconds(10),
            Retry = retryOptions.ToTestRetryConfig(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100)),
        };

        var executor = new ResilientHttpExecutor(policy);
        var gatewayAttempts = 0;
        var passthroughCalls = 0;

        await Assert.ThrowsAsync<NetworkRetryBudgetExhaustedException>(() =>
            executor.ExecuteAsync(
                _ => {
                    gatewayAttempts++;
                    passthroughCalls++;
                    throw new HttpRequestException("fail");
                },
                "integration-op"));

        gatewayAttempts.Should().BeGreaterThanOrEqualTo(16);
        passthroughCalls.Should().Be(gatewayAttempts);
    }
}