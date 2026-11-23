
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Bridge.Tests.Phase7D;

public sealed class V2BridgeConfigTests {
    [Fact]
    public void Defaults_HasExpectedValues() {
        var config = V2BridgeConfig.Defaults;

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
    public void Defaults_ReturnsNewInstance_EachTime() {
        var a = V2BridgeConfig.Defaults;
        var b = V2BridgeConfig.Defaults;
        Assert.NotSame(a, b);
    }

    [Fact]
    public void Validate_InvalidConfig_ReturnsDefaults() {
        // InitRetryMaxAttempts < 1 非法
        var config = new V2BridgeConfig { InitRetryMaxAttempts = 0 };
        var result = V2BridgeConfig.Validate(config);
        Assert.Equal(V2BridgeConfig.Defaults.InitRetryMaxAttempts, result.InitRetryMaxAttempts);
    }

    [Fact]
    public void Validate_HttpTimeoutBelowMin_ReturnsDefaults() {
        var config = new V2BridgeConfig { HttpTimeoutMs = 100 };
        var result = V2BridgeConfig.Validate(config);
        Assert.Equal(V2BridgeConfig.Defaults.HttpTimeoutMs, result.HttpTimeoutMs);
    }

    /// <summary>TASK071 #11 — TS envLessBridgeConfigSchema 上下限对齐（violation 整体回退 DEFAULT）。</summary>
    [Theory]
    [InlineData("HeartbeatIntervalMs", 4999)]     // floor 5s
    [InlineData("HeartbeatIntervalMs", 30001)]    // cap 30s
    [InlineData("TokenRefreshBufferMs", 29999)]   // floor 30s 防紧循环
    [InlineData("TokenRefreshBufferMs", 1800001)] // cap 30min 识别语义反转
    [InlineData("TeardownArchiveTimeoutMs", 499)]
    [InlineData("TeardownArchiveTimeoutMs", 2001)]
    [InlineData("ConnectTimeoutMs", 4999)]
    [InlineData("ConnectTimeoutMs", 60001)]
    [InlineData("UuidDedupBufferSize", 99)]
    [InlineData("UuidDedupBufferSize", 50001)]
    [InlineData("InitRetryMaxAttempts", 11)]
    [InlineData("InitRetryBaseDelayMs", 99)]
    [InlineData("InitRetryMaxDelayMs", 499)]
    [InlineData("HttpTimeoutMs", 1999)]
    public void Validate_TsSchemaBounds_ViolationReturnsDefaults(string property, int value = 0) {
        var config = property switch {
            "HeartbeatIntervalMs" => new V2BridgeConfig { HeartbeatIntervalMs = value },
            "TokenRefreshBufferMs" => new V2BridgeConfig { TokenRefreshBufferMs = value },
            "TeardownArchiveTimeoutMs" => new V2BridgeConfig { TeardownArchiveTimeoutMs = value },
            "ConnectTimeoutMs" => new V2BridgeConfig { ConnectTimeoutMs = value },
            "UuidDedupBufferSize" => new V2BridgeConfig { UuidDedupBufferSize = value },
            "InitRetryMaxAttempts" => new V2BridgeConfig { InitRetryMaxAttempts = value },
            "InitRetryBaseDelayMs" => new V2BridgeConfig { InitRetryBaseDelayMs = value },
            "InitRetryMaxDelayMs" => new V2BridgeConfig { InitRetryMaxDelayMs = value },
            "HttpTimeoutMs" => new V2BridgeConfig { HttpTimeoutMs = value },
            _ => throw new ArgumentException(property),
        };

        var result = V2BridgeConfig.Validate(config);

        Assert.NotEqual(value, property switch {
            "HeartbeatIntervalMs" => result.HeartbeatIntervalMs,
            "TokenRefreshBufferMs" => result.TokenRefreshBufferMs,
            "TeardownArchiveTimeoutMs" => result.TeardownArchiveTimeoutMs,
            "ConnectTimeoutMs" => result.ConnectTimeoutMs,
            "UuidDedupBufferSize" => result.UuidDedupBufferSize,
            "InitRetryMaxAttempts" => result.InitRetryMaxAttempts,
            "InitRetryBaseDelayMs" => result.InitRetryBaseDelayMs,
            "InitRetryMaxDelayMs" => result.InitRetryMaxDelayMs,
            "HttpTimeoutMs" => result.HttpTimeoutMs,
            _ => value,
        });
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0.51)]
    public void Validate_HeartbeatJitterFractionBeyondCap_ReturnsDefaults(double fraction) {
        var config = new V2BridgeConfig { HeartbeatJitterFraction = fraction };

        var result = V2BridgeConfig.Validate(config);

        Assert.Equal(V2BridgeConfig.Defaults.HeartbeatJitterFraction, result.HeartbeatJitterFraction);
    }

    /// <summary>边界内值（floor/cap 恰好命中）应原样通过。</summary>
    [Fact]
    public void Validate_BoundaryValues_Pass() {
        var config = new V2BridgeConfig {
            HeartbeatIntervalMs = 5000,
            TokenRefreshBufferMs = 30_000,
            TeardownArchiveTimeoutMs = 500,
            ConnectTimeoutMs = 60_000,
            UuidDedupBufferSize = 100,
            InitRetryMaxAttempts = 10,
            HeartbeatJitterFraction = 0.5,
        };

        var result = V2BridgeConfig.Validate(config);

        Assert.Same(config, result);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void Validate_NullConfig_Throws() {
        Assert.Throws<ArgumentNullException>(() => V2BridgeConfig.Validate(null!));
    }

    [Fact]
    public void GetConfig_ReturnsValidConfig() {
        var config = V2BridgeConfig.GetConfig();
        Assert.NotNull(config);
        Assert.True(config.HttpTimeoutMs > 0);
    }

    [Fact]
    public void CheckMinVersion_Default_ReturnsNull() {
        // 默认 min_version="0.0.0"，任何版本都满足
        var result = V2BridgeConfig.CheckMinVersion("1.0.0");
        Assert.Null(result);
    }
}