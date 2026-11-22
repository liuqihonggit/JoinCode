namespace Core.Tests.Query.Gate;

/// <summary>
/// TASK102 后续 — 循环上限配置化测试：默认值、环境变量覆盖、非法值回退。
/// </summary>
public sealed class LoopLimitsTests {
    [Fact]
    public void Defaults_AreStable() {
        using var toolEnv = EnvVarScope.Set(JccEnvVar.MaxToolCallIterations.ToValue(), null);
        using var stopEnv = EnvVarScope.Set(JccEnvVar.MaxStopHookReplyRounds.ToValue(), null);

        LoopLimits.MaxToolCallIterations.Should().Be(LoopLimits.DefaultMaxToolCallIterations);
        LoopLimits.MaxStopHookReplyRounds.Should().Be(LoopLimits.DefaultMaxStopHookReplyRounds);
        new QueryEngineConfig().MaxToolCallIterations.Should().Be(LoopLimits.DefaultMaxToolCallIterations);
        new QueryEngineConfig().MaxStopHookReplyRounds.Should().Be(LoopLimits.DefaultMaxStopHookReplyRounds);
    }

    [Fact]
    public void EnvironmentOverrides_AreConsumedByBothLoopsConfig() {
        using var toolEnv = EnvVarScope.Set(JccEnvVar.MaxToolCallIterations.ToValue(), "64");
        using var stopEnv = EnvVarScope.Set(JccEnvVar.MaxStopHookReplyRounds.ToValue(), "9");

        LoopLimits.MaxToolCallIterations.Should().Be(64);
        LoopLimits.MaxStopHookReplyRounds.Should().Be(9);
        new QueryEngineConfig().MaxToolCallIterations.Should().Be(64);
        new QueryEngineConfig().MaxStopHookReplyRounds.Should().Be(9);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("")]
    public void InvalidValues_FallBackToDefaults(string raw) {
        using var toolEnv = EnvVarScope.Set(JccEnvVar.MaxToolCallIterations.ToValue(), raw);
        using var stopEnv = EnvVarScope.Set(JccEnvVar.MaxStopHookReplyRounds.ToValue(), raw);

        LoopLimits.MaxToolCallIterations.Should().Be(LoopLimits.DefaultMaxToolCallIterations);
        LoopLimits.MaxStopHookReplyRounds.Should().Be(LoopLimits.DefaultMaxStopHookReplyRounds);
    }
}
