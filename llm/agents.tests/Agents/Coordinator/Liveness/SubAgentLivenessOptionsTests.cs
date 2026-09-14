namespace Sync.Tests.Agents.Coordinator.Liveness;

/// <summary>
/// SubAgentLivenessOptions 单元测试 — 验证配置校验（ADR 0106）
/// </summary>
public sealed class SubAgentLivenessOptionsTests
{
    [Fact]
    public void DefaultValues_AreValid()
    {
        var options = new SubAgentLivenessOptions();
        var act = () => options.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Default_AgentTimeoutSeconds_Is300()
    {
        var options = new SubAgentLivenessOptions();
        options.AgentTimeoutSeconds.Should().Be(300);
    }

    [Fact]
    public void Default_IdleThresholdSeconds_Is30()
    {
        var options = new SubAgentLivenessOptions();
        options.IdleThresholdSeconds.Should().Be(30);
    }

    [Fact]
    public void Default_CompletionCheckThreshold_Is08()
    {
        var options = new SubAgentLivenessOptions();
        options.CompletionCheckThreshold.Should().Be(0.8);
    }

    [Fact]
    public void Validate_NegativeAgentTimeout_Throws()
    {
        var options = new SubAgentLivenessOptions { AgentTimeoutSeconds = -1 };
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_ZeroIdleThreshold_Throws()
    {
        var options = new SubAgentLivenessOptions { IdleThresholdSeconds = 0 };
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_CompletionCheckThresholdAbove1_Throws()
    {
        var options = new SubAgentLivenessOptions { CompletionCheckThreshold = 1.5 };
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_ZeroPoolMax_Allowed()
    {
        var options = new SubAgentLivenessOptions { PoolMaxSize = 0 };
        var act = () => options.Validate();
        act.Should().NotThrow(); // 0=禁用代理池
    }
}
