namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// PluginUnloadOptionsBuilder 单元测试 — 验证构建器链式调用、预设工厂和 Build 结果
/// <para>确定性测试:不依赖时序/IO,纯字段操作</para>
/// </summary>
public sealed class PluginUnloadOptionsBuilderTest {
    // ===== Create 工厂 =====

    [Fact]
    public void Create_ReturnsBuilderWithDefaultValues() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var options = builder.Build();
        options.CooperativeTimeout.Should().Be(TimeSpan.FromSeconds(5));
        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_ReturnsBuilderWithDefaultValues() {
        var builder = PluginUnloadOptionsBuilder.CreateDefault();

        var options = builder.Build();
        options.CooperativeTimeout.Should().Be(TimeSpan.FromSeconds(5));
        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_EquivalentToCreate() {
        var a = PluginUnloadOptionsBuilder.Create().Build();
        var b = PluginUnloadOptionsBuilder.CreateDefault().Build();

        a.Should().BeEquivalentTo(b);
    }

    // ===== 预设工厂 =====

    [Fact]
    public void CreateFast_ReturnsOneSecondTimeoutAndForceUnload() {
        var options = PluginUnloadOptionsBuilder.CreateFast().Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.FromSeconds(1));
        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void CreateGraceful_ReturnsTenSecondsTimeoutAndNoForceUnload() {
        var options = PluginUnloadOptionsBuilder.CreateGraceful().Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.ForceAlcUnloadOnTimeout.Should().BeFalse();
    }

    [Fact]
    public void CreateForce_ReturnsZeroTimeoutAndForceUnload() {
        var options = PluginUnloadOptionsBuilder.CreateForce().Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.Zero);
        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void CreateFast_MatchesFastPreset() {
        var builder = PluginUnloadOptionsBuilder.CreateFast().Build();

        builder.Should().BeEquivalentTo(PluginUnloadOptions.Fast);
    }

    [Fact]
    public void CreateGraceful_MatchesGracefulPreset() {
        var builder = PluginUnloadOptionsBuilder.CreateGraceful().Build();

        builder.Should().BeEquivalentTo(PluginUnloadOptions.Graceful);
    }

    [Fact]
    public void CreateForce_MatchesForcePreset() {
        var builder = PluginUnloadOptionsBuilder.CreateForce().Build();

        builder.Should().BeEquivalentTo(PluginUnloadOptions.Force);
    }

    [Fact]
    public void CreateDefault_MatchesDefaultPreset() {
        var builder = PluginUnloadOptionsBuilder.CreateDefault().Build();

        builder.Should().BeEquivalentTo(PluginUnloadOptions.Default);
    }

    // ===== WithTimeout 链式调用 =====

    [Fact]
    public void WithTimeout_SetsCooperativeTimeout() {
        var timeout = TimeSpan.FromMinutes(2);

        var options = PluginUnloadOptionsBuilder.Create().WithTimeout(timeout).Build();

        options.CooperativeTimeout.Should().Be(timeout);
    }

    [Fact]
    public void WithTimeout_ReturnsSameBuilderInstance_ForChaining() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var returned = builder.WithTimeout(TimeSpan.FromSeconds(10));

        returned.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithTimeout_ZeroTimeout_SetsZero() {
        var options = PluginUnloadOptionsBuilder.Create().WithTimeout(TimeSpan.Zero).Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void WithTimeout_NegativeTimeout_AcceptsValue() {
        // 构建器不做范围校验,仅赋值(与文档契约一致)
        var options = PluginUnloadOptionsBuilder.Create().WithTimeout(TimeSpan.FromSeconds(-1)).Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.FromSeconds(-1));
    }

    // ===== WithTimeoutSeconds =====

    [Fact]
    public void WithTimeoutSeconds_SetsTimeoutFromSeconds() {
        var options = PluginUnloadOptionsBuilder.Create().WithTimeoutSeconds(30).Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void WithTimeoutSeconds_ReturnsSameBuilderInstance() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var returned = builder.WithTimeoutSeconds(30);

        returned.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithTimeoutSeconds_Zero_SetsZero() {
        var options = PluginUnloadOptionsBuilder.Create().WithTimeoutSeconds(0).Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.Zero);
    }

    // ===== WithTimeoutMilliseconds =====

    [Fact]
    public void WithTimeoutMilliseconds_SetsTimeoutFromMilliseconds() {
        var options = PluginUnloadOptionsBuilder.Create().WithTimeoutMilliseconds(500).Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void WithTimeoutMilliseconds_ReturnsSameBuilderInstance() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var returned = builder.WithTimeoutMilliseconds(500);

        returned.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithTimeoutMilliseconds_OverlapsWithSecondsCorrectly() {
        var options = PluginUnloadOptionsBuilder.Create()
            .WithTimeoutSeconds(2)
            .WithTimeoutMilliseconds(500)
            .Build();

        // 后者覆盖前者
        options.CooperativeTimeout.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    // ===== WithForceUnload =====

    [Fact]
    public void WithForceUnload_True_SetsForceUnloadTrue() {
        var options = PluginUnloadOptionsBuilder.Create().WithForceUnload(true).Build();

        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void WithForceUnload_False_SetsForceUnloadFalse() {
        var options = PluginUnloadOptionsBuilder.Create().WithForceUnload(false).Build();

        options.ForceAlcUnloadOnTimeout.Should().BeFalse();
    }

    [Fact]
    public void WithForceUnload_ReturnsSameBuilderInstance() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var returned = builder.WithForceUnload(true);

        returned.Should().BeSameAs(builder);
    }

    // ===== ForceUnload / NoForceUnload =====

    [Fact]
    public void ForceUnload_SetsForceUnloadTrue() {
        var options = PluginUnloadOptionsBuilder.Create().NoForceUnload().ForceUnload().Build();

        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void ForceUnload_ReturnsSameBuilderInstance() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var returned = builder.ForceUnload();

        returned.Should().BeSameAs(builder);
    }

    [Fact]
    public void NoForceUnload_SetsForceUnloadFalse() {
        var options = PluginUnloadOptionsBuilder.Create().NoForceUnload().Build();

        options.ForceAlcUnloadOnTimeout.Should().BeFalse();
    }

    [Fact]
    public void NoForceUnload_ReturnsSameBuilderInstance() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var returned = builder.NoForceUnload();

        returned.Should().BeSameAs(builder);
    }

    [Fact]
    public void NoForceUnload_ThenForceUnload_TogglesToTrue() {
        var options = PluginUnloadOptionsBuilder.Create()
            .NoForceUnload()
            .ForceUnload()
            .Build();

        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void ForceUnload_ThenNoForceUnload_TogglesToFalse() {
        var options = PluginUnloadOptionsBuilder.Create()
            .ForceUnload()
            .NoForceUnload()
            .Build();

        options.ForceAlcUnloadOnTimeout.Should().BeFalse();
    }

    // ===== Immediate =====

    [Fact]
    public void Immediate_SetsZeroTimeout() {
        var options = PluginUnloadOptionsBuilder.Create().Immediate().Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Immediate_ReturnsSameBuilderInstance() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var returned = builder.Immediate();

        returned.Should().BeSameAs(builder);
    }

    [Fact]
    public void Immediate_AfterWithTimeout_OverridesToZero() {
        var options = PluginUnloadOptionsBuilder.Create()
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Immediate()
            .Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.Zero);
    }

    // ===== 链式组合 =====

    [Fact]
    public void Chaining_AllMethods_ReturnsSameBuilderAndCorrectResult() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var returned = builder
            .WithTimeout(TimeSpan.FromSeconds(20))
            .WithForceUnload(false)
            .Immediate()
            .ForceUnload();

        returned.Should().BeSameAs(builder);
        var options = builder.Build();
        options.CooperativeTimeout.Should().Be(TimeSpan.Zero);
        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void Chaining_CreateForceThenOverrideTimeout_PreservesForceUnload() {
        var options = PluginUnloadOptionsBuilder.CreateForce()
            .WithTimeoutSeconds(30)
            .Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    [Fact]
    public void Chaining_CreateGracefulThenForceUnload_OverridesForceUnload() {
        var options = PluginUnloadOptionsBuilder.CreateGraceful()
            .ForceUnload()
            .Build();

        options.CooperativeTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.ForceAlcUnloadOnTimeout.Should().BeTrue();
    }

    // ===== Build =====

    [Fact]
    public void Build_ReturnsNewInstance_EachCall() {
        var builder = PluginUnloadOptionsBuilder.Create();

        var a = builder.Build();
        var b = builder.Build();

        a.Should().NotBeSameAs(b);
        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Build_AfterNoMutation_ReturnsDefaultEquivalent() {
        var options = PluginUnloadOptionsBuilder.Create().Build();

        options.Should().BeEquivalentTo(PluginUnloadOptions.Default);
    }

    [Fact]
    public void Build_ProducesSealedPluginUnloadOptions() {
        var options = PluginUnloadOptionsBuilder.Create().Build();

        options.Should().BeOfType<PluginUnloadOptions>();
    }
}
