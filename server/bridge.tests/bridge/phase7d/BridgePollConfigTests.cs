
namespace Bridge.Tests.Phase7D;

public sealed class BridgePollConfigTests
{
    /// <summary>每个测试前重置缓存，避免状态泄漏</summary>
    public BridgePollConfigTests()
    {
        BridgePollConfig.ResetCache();
    }

    #region Defaults 默认值

    [Fact]
    public void Defaults_HasExpectedValues()
    {
        var config = BridgePollIntervalConfig.Defaults;

        Assert.Equal(2000, config.PollIntervalMsNotAtCapacity);
        Assert.Equal(600000, config.PollIntervalMsAtCapacity);
        Assert.Equal(0, config.NonExclusiveHeartbeatIntervalMs);
        Assert.Equal(2000, config.MultisessionPollIntervalMsNotAtCapacity);
        Assert.Equal(2000, config.MultisessionPollIntervalMsPartialCapacity);
        Assert.Equal(600000, config.MultisessionPollIntervalMsAtCapacity);
        Assert.Equal(5000, config.ReclaimOlderThanMs);
        Assert.Equal(120000, config.SessionKeepaliveIntervalV2Ms);
    }

    [Fact]
    public void Defaults_ReturnsNewInstance_EachTime()
    {
        var a = BridgePollIntervalConfig.Defaults;
        var b = BridgePollIntervalConfig.Defaults;
        Assert.NotSame(a, b);
    }

    #endregion

    #region GetPollIntervalConfig

    [Fact]
    public void GetPollIntervalConfig_ReturnsValidConfig()
    {
        var config = BridgePollConfig.GetPollIntervalConfig();
        Assert.NotNull(config);
        // 默认配置通过验证（at_capacity=600000 > 0 满足 liveness）
        Assert.Equal(2000, config.PollIntervalMsNotAtCapacity);
    }

    #endregion

    #region ValidateConfig — 单字段验证

    [Fact]
    public void Validate_PollIntervalBelowMin_ReturnsDefaults()
    {
        var config = new BridgePollIntervalConfig { PollIntervalMsNotAtCapacity = 50 };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.PollIntervalMsNotAtCapacity, result.PollIntervalMsNotAtCapacity);
    }

    [Fact]
    public void Validate_AtCapacityZero_IsValid()
    {
        // at_capacity=0 合法（禁用），但需要心跳启用以满足 liveness
        var config = new BridgePollIntervalConfig
        {
            PollIntervalMsAtCapacity = 0,
            NonExclusiveHeartbeatIntervalMs = 1000, // 心跳启用满足 liveness
        };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(0, result.PollIntervalMsAtCapacity);
    }

    [Fact]
    public void Validate_AtCapacityBelowMin_ReturnsDefaults()
    {
        var config = new BridgePollIntervalConfig { PollIntervalMsAtCapacity = 50 };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.PollIntervalMsAtCapacity, result.PollIntervalMsAtCapacity);
    }

    [Fact]
    public void Validate_HeartbeatNegative_ReturnsDefaults()
    {
        var config = new BridgePollIntervalConfig { NonExclusiveHeartbeatIntervalMs = -1 };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.NonExclusiveHeartbeatIntervalMs, result.NonExclusiveHeartbeatIntervalMs);
    }

    [Fact]
    public void Validate_MultisessionPollBelowMin_ReturnsDefaults()
    {
        var config = new BridgePollIntervalConfig { MultisessionPollIntervalMsNotAtCapacity = 50 };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.MultisessionPollIntervalMsNotAtCapacity, result.MultisessionPollIntervalMsNotAtCapacity);
    }

    [Fact]
    public void Validate_MultisessionPartialBelowMin_ReturnsDefaults()
    {
        var config = new BridgePollIntervalConfig { MultisessionPollIntervalMsPartialCapacity = 50 };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.MultisessionPollIntervalMsPartialCapacity, result.MultisessionPollIntervalMsPartialCapacity);
    }

    [Fact]
    public void Validate_MultisessionAtCapacityBelowMin_ReturnsDefaults()
    {
        var config = new BridgePollIntervalConfig { MultisessionPollIntervalMsAtCapacity = 50 };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.MultisessionPollIntervalMsAtCapacity, result.MultisessionPollIntervalMsAtCapacity);
    }

    [Fact]
    public void Validate_ReclaimBelowOne_ReturnsDefaults()
    {
        // 对齐 TS 端 .min(1)
        var config = new BridgePollIntervalConfig { ReclaimOlderThanMs = 0 };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.ReclaimOlderThanMs, result.ReclaimOlderThanMs);
    }

    [Fact]
    public void Validate_KeepaliveNegative_ReturnsDefaults()
    {
        var config = new BridgePollIntervalConfig { SessionKeepaliveIntervalV2Ms = -1 };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.SessionKeepaliveIntervalV2Ms, result.SessionKeepaliveIntervalV2Ms);
    }

    [Fact]
    public void Validate_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BridgePollConfig.ValidateConfig(null!));
    }

    #endregion

    #region ValidateConfig — 对象级 refine (liveness)

    [Fact]
    public void Validate_SingleSessionLiveness_BothDisabled_ReturnsDefaults()
    {
        // 心跳=0 且 at_capacity=0 → 单会话 liveness 失败
        var config = new BridgePollIntervalConfig
        {
            NonExclusiveHeartbeatIntervalMs = 0,
            PollIntervalMsAtCapacity = 0,
        };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.PollIntervalMsAtCapacity, result.PollIntervalMsAtCapacity);
    }

    [Fact]
    public void Validate_MultisessionLiveness_BothDisabled_ReturnsDefaults()
    {
        // 心跳=0 且 multisession_at_capacity=0 → 多会话 liveness 失败
        var config = new BridgePollIntervalConfig
        {
            NonExclusiveHeartbeatIntervalMs = 0,
            MultisessionPollIntervalMsAtCapacity = 0,
            PollIntervalMsAtCapacity = 600000, // 单会话 liveness 通过
        };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(BridgePollIntervalConfig.Defaults.MultisessionPollIntervalMsAtCapacity, result.MultisessionPollIntervalMsAtCapacity);
    }

    [Fact]
    public void Validate_Liveness_HeartbeatEnabled_Passes()
    {
        // 心跳启用 → 两个 liveness 约束都通过
        var config = new BridgePollIntervalConfig
        {
            NonExclusiveHeartbeatIntervalMs = 1000,
            PollIntervalMsAtCapacity = 0,
            MultisessionPollIntervalMsAtCapacity = 0,
        };
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(1000, result.NonExclusiveHeartbeatIntervalMs);
    }

    [Fact]
    public void Validate_ValidConfig_ReturnsAsIs()
    {
        var config = BridgePollIntervalConfig.Defaults;
        var result = BridgePollConfig.ValidateConfig(config);
        Assert.Equal(config.PollIntervalMsNotAtCapacity, result.PollIntervalMsNotAtCapacity);
    }

    #endregion
}
