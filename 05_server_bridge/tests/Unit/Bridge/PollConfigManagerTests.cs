
namespace Bridge.Tests;

/// <summary>
/// PollConfigManager 单元测试
/// 测试默认配置、配置更新、指数退避计算和重置
/// </summary>
public sealed class PollConfigManagerTests
{
    private static PollConfigManager CreateSut(PollConfig? initialConfig = null) =>
        new(initialConfig, NullLogger<PollConfigManager>.Instance);

    [Fact]
    public async Task GetCurrentConfigAsync_ShouldReturnDefaultConfig()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var config = await sut.GetCurrentConfigAsync().ConfigureAwait(true);

        // Assert
        config.Should().NotBeNull();
        config.IntervalMs.Should().Be(PollConfig.DefaultIntervalMs);
        config.MaxIntervalMs.Should().Be(PollConfig.DefaultMaxIntervalMs);
        config.BackoffMultiplier.Should().Be(PollConfig.DefaultBackoffMultiplier);
        config.JitterPercent.Should().Be(PollConfig.DefaultJitterPercent);
        config.TimeoutMs.Should().Be(PollConfig.DefaultTimeoutMs);
    }

    [Fact]
    public async Task UpdateConfigAsync_ShouldUpdateConfig()
    {
        // Arrange
        var sut = CreateSut();
        var newConfig = new PollConfig
        {
            IntervalMs = 200,
            MaxIntervalMs = 60000,
            BackoffMultiplier = 3.0,
            JitterPercent = 0.2,
            TimeoutMs = 60000
        };

        // Act
        await sut.UpdateConfigAsync(newConfig).ConfigureAwait(true);
        var config = await sut.GetCurrentConfigAsync().ConfigureAwait(true);

        // Assert
        config.IntervalMs.Should().Be(200);
        config.MaxIntervalMs.Should().Be(60000);
        config.BackoffMultiplier.Should().Be(3.0);
        config.JitterPercent.Should().Be(0.2);
        config.TimeoutMs.Should().Be(60000);
    }

    [Fact]
    public async Task CalculateNextIntervalAsync_ShouldReturnBaseInterval_WhenNoErrors()
    {
        // Arrange
        var sut = CreateSut();
        var config = await sut.GetCurrentConfigAsync().ConfigureAwait(true);

        // Act - hasError=false 表示无错误
        var interval = await sut.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(true);

        // Assert - 无错误时，consecutiveErrors=0，baseInterval = IntervalMs * BackoffMultiplier^0 = IntervalMs
        // 应用抖动后，结果在 [IntervalMs * (1 - jitter), IntervalMs * (1 + jitter)] 范围内
        // 但最终结果被 Math.Max(IntervalMs, ...) 限制，所以 >= IntervalMs
        var jitterLow = config.IntervalMs * (1.0 - config.JitterPercent);
        var jitterHigh = config.IntervalMs * (1.0 + config.JitterPercent);
        interval.Should().BeInRange((int)Math.Round(jitterLow), (int)Math.Round(jitterHigh));
    }

    [Fact]
    public async Task CalculateNextIntervalAsync_ShouldIncreaseInterval_WhenErrorsOccur()
    {
        // Arrange
        var sut = CreateSut();
        var config = await sut.GetCurrentConfigAsync().ConfigureAwait(true);

        // Act - 连续多次错误
        var intervalNoError = await sut.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(true);
        var interval1Error = await sut.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(true);
        var interval2Errors = await sut.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(true);

        // Assert - 错误越多，间隔应越大（考虑抖动后趋势）
        // consecutiveErrors=1: baseInterval = 100 * 1.5^1 = 150
        // consecutiveErrors=2: baseInterval = 100 * 1.5^2 = 225
        // 抖动范围约 10%，但趋势应明显增大
        interval1Error.Should().BeGreaterThan(intervalNoError - 20); // 允许抖动偏差
        interval2Errors.Should().BeGreaterThan(intervalNoError);
    }

    [Fact]
    public async Task CalculateNextIntervalAsync_ShouldNotExceedMaxInterval()
    {
        // Arrange - 使用小 MaxIntervalMs 便于测试
        var sut = CreateSut(new PollConfig
        {
            IntervalMs = 100,
            MaxIntervalMs = 500,
            BackoffMultiplier = 10.0,
            JitterPercent = 0.0 // 关闭抖动以便精确断言
        });

        // Act - 多次错误使退避超过 MaxIntervalMs
        for (var i = 0; i < 10; i++)
        {
            await sut.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(true);
        }

        var interval = await sut.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(true);

        // Assert - 即使大量错误，间隔也不应超过 MaxIntervalMs（考虑抖动=0）
        interval.Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public async Task ResetToDefaultAsync_ShouldRestoreDefaultConfig()
    {
        // Arrange
        var sut = CreateSut();
        var customConfig = new PollConfig
        {
            IntervalMs = 999,
            MaxIntervalMs = 99999,
            BackoffMultiplier = 5.0,
            JitterPercent = 0.5,
            TimeoutMs = 99999
        };
        await sut.UpdateConfigAsync(customConfig).ConfigureAwait(true);

        // Act
        await sut.ResetToDefaultAsync().ConfigureAwait(true);
        var config = await sut.GetCurrentConfigAsync().ConfigureAwait(true);

        // Assert
        config.IntervalMs.Should().Be(PollConfig.DefaultIntervalMs);
        config.MaxIntervalMs.Should().Be(PollConfig.DefaultMaxIntervalMs);
        config.BackoffMultiplier.Should().Be(PollConfig.DefaultBackoffMultiplier);
        config.JitterPercent.Should().Be(PollConfig.DefaultJitterPercent);
        config.TimeoutMs.Should().Be(PollConfig.DefaultTimeoutMs);
    }
}
