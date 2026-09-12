namespace Dream.Tests.Models;

/// <summary>
/// 自动做梦配置构建器单元测试
/// </summary>
public sealed class AutoDreamConfigBuilderTests
{
    [Fact]
    public void Create_ReturnsBuilder()
    {
        var builder = AutoDreamConfigBuilder.Create();

        Assert.NotNull(builder);
    }

    [Fact]
    public void WithMinHours_SetsValue()
    {
        var config = AutoDreamConfigBuilder.Create().WithMinHours(12).Build();

        Assert.Equal(12, config.MinHours);
    }

    [Fact]
    public void WithMinSessions_SetsValue()
    {
        var config = AutoDreamConfigBuilder.Create().WithMinSessions(3).Build();

        Assert.Equal(3, config.MinSessions);
    }

    [Fact]
    public void WithSessionScanInterval_SetsValue()
    {
        var config = AutoDreamConfigBuilder.Create().WithSessionScanInterval(12345).Build();

        Assert.Equal(12345, config.SessionScanIntervalMs);
    }

    [Fact]
    public void WithSessionScanIntervalMinutes_SetsValue()
    {
        var config = AutoDreamConfigBuilder.Create().WithSessionScanIntervalMinutes(5).Build();

        Assert.Equal(5 * 60 * 1000, config.SessionScanIntervalMs);
    }

    [Fact]
    public void Enable_SetsEnabledTrue()
    {
        var config = AutoDreamConfigBuilder.Create().Disable().Enable().Build();

        Assert.True(config.Enabled);
    }

    [Fact]
    public void Disable_SetsEnabledFalse()
    {
        var config = AutoDreamConfigBuilder.Create().Enable().Disable().Build();

        Assert.False(config.Enabled);
    }

    [Fact]
    public void WithEnabled_SetsValue()
    {
        var config = AutoDreamConfigBuilder.Create().WithEnabled(false).Build();

        Assert.False(config.Enabled);
    }

    [Fact]
    public void EnableAutoMemory_SetsAutoMemoryEnabledTrue()
    {
        var config = AutoDreamConfigBuilder.Create().DisableAutoMemory().EnableAutoMemory().Build();

        Assert.True(config.AutoMemoryEnabled);
    }

    [Fact]
    public void DisableAutoMemory_SetsAutoMemoryEnabledFalse()
    {
        var config = AutoDreamConfigBuilder.Create().EnableAutoMemory().DisableAutoMemory().Build();

        Assert.False(config.AutoMemoryEnabled);
    }

    [Fact]
    public void WithAutoMemoryEnabled_SetsValue()
    {
        var config = AutoDreamConfigBuilder.Create().WithAutoMemoryEnabled(false).Build();

        Assert.False(config.AutoMemoryEnabled);
    }

    [Fact]
    public void WithAutoMemoryPath_SetsValue()
    {
        var config = AutoDreamConfigBuilder.Create().WithAutoMemoryPath("/memory").Build();

        Assert.Equal("/memory", config.AutoMemoryPath);
    }

    [Fact]
    public void WithProjectDir_SetsValue()
    {
        var config = AutoDreamConfigBuilder.Create().WithProjectDir("/project").Build();

        Assert.Equal("/project", config.ProjectDir);
    }

    [Fact]
    public void UseHighFrequencyMode_SetsValues()
    {
        var config = AutoDreamConfigBuilder.Create().UseHighFrequencyMode().Build();

        Assert.Equal(1, config.MinHours);
        Assert.Equal(2, config.MinSessions);
        Assert.Equal(60 * 1000, config.SessionScanIntervalMs);
    }

    [Fact]
    public void UseLowFrequencyMode_SetsValues()
    {
        var config = AutoDreamConfigBuilder.Create().UseLowFrequencyMode().Build();

        Assert.Equal(48, config.MinHours);
        Assert.Equal(10, config.MinSessions);
        Assert.Equal(60 * 60 * 1000, config.SessionScanIntervalMs);
    }

    [Fact]
    public void UseBalancedMode_SetsValues()
    {
        var config = AutoDreamConfigBuilder.Create().UseLowFrequencyMode().UseBalancedMode().Build();

        Assert.Equal(24, config.MinHours);
        Assert.Equal(5, config.MinSessions);
        Assert.Equal(10 * 60 * 1000, config.SessionScanIntervalMs);
    }

    [Fact]
    public void Build_ReturnsConfigWithDefaults()
    {
        var config = AutoDreamConfigBuilder.Create().Build();

        Assert.Equal(24, config.MinHours);
        Assert.Equal(5, config.MinSessions);
        Assert.Equal(10 * 60 * 1000, config.SessionScanIntervalMs);
        Assert.True(config.Enabled);
        Assert.True(config.AutoMemoryEnabled);
    }
}