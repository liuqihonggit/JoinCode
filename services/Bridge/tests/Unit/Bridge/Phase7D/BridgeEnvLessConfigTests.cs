
namespace Bridge.Tests.Phase7D;

public sealed class BridgeEnvLessConfigTests
{
    [Fact]
    public void Defaults_HasExpectedValues()
    {
        var config = BridgeEnvLessConfig.Defaults;

        Assert.Equal(3, config.InitRetryMaxAttempts);
        Assert.Equal(500, config.InitRetryBaseDelayMs);
        Assert.Equal(0.25, config.InitRetryJitterFraction);
        Assert.Equal(4000, config.InitRetryMaxDelayMs);
        Assert.Equal(10000, config.HttpTimeoutMs);
        Assert.Equal(2000, config.UuidDedupBufferSize);
        Assert.Equal(20000, config.HeartbeatIntervalMs);
        Assert.Equal(0.1, config.HeartbeatJitterFraction);
        Assert.Equal(300000, config.TokenRefreshBufferMs);
        Assert.Equal(1500, config.TeardownArchiveTimeoutMs);
        Assert.Equal(15000, config.ConnectTimeoutMs);
        Assert.Equal("0.0.0", config.MinVersion);
        Assert.False(config.ShouldShowAppUpgradeMessage);
    }

    [Fact]
    public void Defaults_ReturnsNewInstance_EachTime()
    {
        var a = BridgeEnvLessConfig.Defaults;
        var b = BridgeEnvLessConfig.Defaults;
        Assert.NotSame(a, b);
    }

    [Fact]
    public void Validate_InvalidConfig_ReturnsDefaults()
    {
        // InitRetryMaxAttempts < 1 非法
        var config = new BridgeEnvLessConfig { InitRetryMaxAttempts = 0 };
        var result = BridgeEnvLessConfig.Validate(config);
        Assert.Equal(BridgeEnvLessConfig.Defaults.InitRetryMaxAttempts, result.InitRetryMaxAttempts);
    }

    [Fact]
    public void Validate_HttpTimeoutBelowMin_ReturnsDefaults()
    {
        var config = new BridgeEnvLessConfig { HttpTimeoutMs = 100 };
        var result = BridgeEnvLessConfig.Validate(config);
        Assert.Equal(BridgeEnvLessConfig.Defaults.HttpTimeoutMs, result.HttpTimeoutMs);
    }

    [Fact]
    public void Validate_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BridgeEnvLessConfig.Validate(null!));
    }

    [Fact]
    public void GetConfig_ReturnsValidConfig()
    {
        var config = BridgeEnvLessConfig.GetConfig();
        Assert.NotNull(config);
        Assert.True(config.HttpTimeoutMs > 0);
    }

    [Fact]
    public void CheckMinVersion_Default_ReturnsNull()
    {
        // 默认 min_version="0.0.0"，任何版本都满足
        var result = BridgeEnvLessConfig.CheckMinVersion("1.0.0");
        Assert.Null(result);
    }
}
