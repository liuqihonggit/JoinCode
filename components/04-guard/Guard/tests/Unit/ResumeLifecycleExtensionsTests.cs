namespace Host.Tests.ChatCommands;

/// <summary>
/// ResumeLifecycle 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / ResumeLifecycleConstants 常量值
/// </summary>
public sealed class ResumeLifecycleExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_Pause_Should_Return_pause()
    {
        ResumeLifecycle.Pause.ToValue().Should().Be("pause");
    }

    [Fact]
    public void ToValue_Resume_Should_Return_resume()
    {
        ResumeLifecycle.Resume.ToValue().Should().Be("resume");
    }

    [Fact]
    public void ToValue_Clear_Should_Return_clear()
    {
        ResumeLifecycle.Clear.ToValue().Should().Be("clear");
    }

    [Fact]
    public void ToValue_Stop_Should_Return_stop()
    {
        ResumeLifecycle.Stop.ToValue().Should().Be("stop");
    }

    [Fact]
    public void ToValue_Off_Should_Return_off()
    {
        ResumeLifecycle.Off.ToValue().Should().Be("off");
    }

    [Fact]
    public void ToValue_Reset_Should_Return_reset()
    {
        ResumeLifecycle.Reset.ToValue().Should().Be("reset");
    }

    [Fact]
    public void ToValue_Cancel_Should_Return_cancel()
    {
        ResumeLifecycle.Cancel.ToValue().Should().Be("cancel");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("pause", ResumeLifecycle.Pause)]
    [InlineData("resume", ResumeLifecycle.Resume)]
    [InlineData("clear", ResumeLifecycle.Clear)]
    [InlineData("stop", ResumeLifecycle.Stop)]
    [InlineData("off", ResumeLifecycle.Off)]
    [InlineData("reset", ResumeLifecycle.Reset)]
    [InlineData("cancel", ResumeLifecycle.Cancel)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, ResumeLifecycle expected)
    {
        ResumeLifecycleExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void FromValue_Should_Be_CaseInsensitive()
    {
        ResumeLifecycleExtensions.FromValue("PAUSE").Should().Be(ResumeLifecycle.Pause);
        ResumeLifecycleExtensions.FromValue("Resume").Should().Be(ResumeLifecycle.Resume);
        ResumeLifecycleExtensions.FromValue("CLEAR").Should().Be(ResumeLifecycle.Clear);
        ResumeLifecycleExtensions.FromValue("Stop").Should().Be(ResumeLifecycle.Stop);
        ResumeLifecycleExtensions.FromValue("CANCEL").Should().Be(ResumeLifecycle.Cancel);
    }

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        ResumeLifecycleExtensions.FromValue("invalid").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        ResumeLifecycleExtensions.FromValue("").Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        ResumeLifecycleExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(ResumeLifecycle.Pause, true)]
    [InlineData(ResumeLifecycle.Resume, true)]
    [InlineData(ResumeLifecycle.Clear, true)]
    [InlineData(ResumeLifecycle.Stop, true)]
    [InlineData(ResumeLifecycle.Off, true)]
    [InlineData(ResumeLifecycle.Reset, true)]
    [InlineData(ResumeLifecycle.Cancel, true)]
    public void IsDefined_AllValidValues_Should_Return_True(ResumeLifecycle value, bool expected)
    {
        ResumeLifecycleExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== ResumeLifecycleConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_EnumValues()
    {
        ResumeLifecycleConstants.Pause.Should().Be("pause");
        ResumeLifecycleConstants.Resume.Should().Be("resume");
        ResumeLifecycleConstants.Clear.Should().Be("clear");
        ResumeLifecycleConstants.Stop.Should().Be("stop");
        ResumeLifecycleConstants.Off.Should().Be("off");
        ResumeLifecycleConstants.Reset.Should().Be("reset");
        ResumeLifecycleConstants.Cancel.Should().Be("cancel");
    }

    // ===== 往返一致性测试 =====

    [Theory]
    [InlineData(ResumeLifecycle.Pause)]
    [InlineData(ResumeLifecycle.Resume)]
    [InlineData(ResumeLifecycle.Clear)]
    [InlineData(ResumeLifecycle.Stop)]
    [InlineData(ResumeLifecycle.Off)]
    [InlineData(ResumeLifecycle.Reset)]
    [InlineData(ResumeLifecycle.Cancel)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(ResumeLifecycle value)
    {
        var str = value.ToValue();
        ResumeLifecycleExtensions.FromValue(str).Should().Be(value);
    }

    // ===== 数量验证 =====

    [Fact]
    public void AllValues_Should_Be_7()
    {
        var values = Enum.GetValues<ResumeLifecycle>();
        values.Should().HaveCount(7);
    }
}
