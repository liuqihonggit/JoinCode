namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// ConnectionManager.CalculateReconnectDelay 确定性测试 — 指数退避公式 Min(base*2^(n-1), max)。
/// </summary>
public class ConnectionManagerTest {
    private static ConnectionManager CreateManager(int baseDelay, int maxDelay) {
        var config = new TransportConfiguration {
            ReconnectDelayMs = baseDelay,
            MaxReconnectDelayMs = maxDelay,
        };
        return new ConnectionManager(config);
    }

    /// <summary>第 1 次重连延迟 = base * 2^0 = base。</summary>
    [Fact]
    public async Task CalculateReconnectDelay_FirstAttempt_ReturnsBase() {
        await using var mgr = CreateManager(100, 1000);

        mgr.CalculateReconnectDelay(1).TotalMilliseconds.Should().Be(100);
    }

    /// <summary>第 2 次重连延迟 = base * 2^1 = 2 * base。</summary>
    [Fact]
    public async Task CalculateReconnectDelay_SecondAttempt_ReturnsDoubleBase() {
        await using var mgr = CreateManager(100, 1000);

        mgr.CalculateReconnectDelay(2).TotalMilliseconds.Should().Be(200);
    }

    /// <summary>第 3 次重连延迟 = base * 2^2 = 4 * base。</summary>
    [Fact]
    public async Task CalculateReconnectDelay_ThirdAttempt_ReturnsQuadrupleBase() {
        await using var mgr = CreateManager(100, 1000);

        mgr.CalculateReconnectDelay(3).TotalMilliseconds.Should().Be(400);
    }

    /// <summary>指数退避被 MaxReconnectDelayMs 钳制。</summary>
    [Fact]
    public async Task CalculateReconnectDelay_ExceedsMax_ClampedToMax() {
        await using var mgr = CreateManager(100, 1000);

        // 100 * 2^4 = 1600 > 1000 → 钳制为 1000
        mgr.CalculateReconnectDelay(5).TotalMilliseconds.Should().Be(1000);
    }

    /// <summary>大 attempt 值仍被钳制到 max。</summary>
    [Fact]
    public async Task CalculateReconnectDelay_LargeAttempt_ClampedToMax() {
        await using var mgr = CreateManager(100, 1000);

        // 100 * 2^9 = 51200 → 钳制为 1000
        mgr.CalculateReconnectDelay(10).TotalMilliseconds.Should().Be(1000);
    }

    /// <summary>base 等于 max 时所有 attempt 都返回 base。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task CalculateReconnectDelay_BaseEqualsMax_AlwaysReturnsBase(int attempt) {
        await using var mgr = CreateManager(500, 500);

        mgr.CalculateReconnectDelay(attempt).TotalMilliseconds.Should().Be(500);
    }

    /// <summary>默认配置（base=1000, max=30000）下前几次退避符合 2 的幂序列。</summary>
    [Fact]
    public async Task CalculateReconnectDelay_DefaultConfig_FollowsPowerOfTwoSequence() {
        await using var mgr = CreateManager(
            TransportConfiguration.DefaultReconnectDelayMs,
            TransportConfiguration.DefaultMaxReconnectDelayMs);

        mgr.CalculateReconnectDelay(1).TotalMilliseconds.Should().Be(1000);
        mgr.CalculateReconnectDelay(2).TotalMilliseconds.Should().Be(2000);
        mgr.CalculateReconnectDelay(3).TotalMilliseconds.Should().Be(4000);
        mgr.CalculateReconnectDelay(4).TotalMilliseconds.Should().Be(8000);
        mgr.CalculateReconnectDelay(5).TotalMilliseconds.Should().Be(16000);
        // 1000 * 2^5 = 32000 > 30000 → 钳制
        mgr.CalculateReconnectDelay(6).TotalMilliseconds.Should().Be(30000);
    }
}
