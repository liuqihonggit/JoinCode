namespace Host.Tests.ChatCommands;

/// <summary>
/// PlanSubCommand 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / PlanSubCommandConstants 常量值
/// </summary>
public sealed class PlanSubCommandExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_On_Should_Return_on()
    {
        PlanSubCommand.On.ToValue().Should().Be("on");
    }

    [Fact]
    public void ToValue_Enter_Should_Return_enter()
    {
        PlanSubCommand.Enter.ToValue().Should().Be("enter");
    }

    [Fact]
    public void ToValue_Off_Should_Return_off()
    {
        PlanSubCommand.Off.ToValue().Should().Be("off");
    }

    [Fact]
    public void ToValue_Exit_Should_Return_exit()
    {
        PlanSubCommand.Exit.ToValue().Should().Be("exit");
    }

    [Fact]
    public void ToValue_Status_Should_Return_status()
    {
        PlanSubCommand.Status.ToValue().Should().Be("status");
    }

    [Fact]
    public void ToValue_Open_Should_Return_open()
    {
        PlanSubCommand.Open.ToValue().Should().Be("open");
    }

    [Fact]
    public void ToValue_Toggle_Should_Return_toggle()
    {
        PlanSubCommand.Toggle.ToValue().Should().Be("toggle");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("on", PlanSubCommand.On)]
    [InlineData("enter", PlanSubCommand.Enter)]
    [InlineData("off", PlanSubCommand.Off)]
    [InlineData("exit", PlanSubCommand.Exit)]
    [InlineData("status", PlanSubCommand.Status)]
    [InlineData("open", PlanSubCommand.Open)]
    [InlineData("toggle", PlanSubCommand.Toggle)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, PlanSubCommand expected)
    {
        PlanSubCommandExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void FromValue_Should_Be_CaseInsensitive()
    {
        PlanSubCommandExtensions.FromValue("ON").Should().Be(PlanSubCommand.On);
        PlanSubCommandExtensions.FromValue("ENTER").Should().Be(PlanSubCommand.Enter);
        PlanSubCommandExtensions.FromValue("Status").Should().Be(PlanSubCommand.Status);
        PlanSubCommandExtensions.FromValue("OPEN").Should().Be(PlanSubCommand.Open);
    }

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        PlanSubCommandExtensions.FromValue("invalid").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        PlanSubCommandExtensions.FromValue("").Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        PlanSubCommandExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(PlanSubCommand.On, true)]
    [InlineData(PlanSubCommand.Enter, true)]
    [InlineData(PlanSubCommand.Off, true)]
    [InlineData(PlanSubCommand.Exit, true)]
    [InlineData(PlanSubCommand.Status, true)]
    [InlineData(PlanSubCommand.Open, true)]
    [InlineData(PlanSubCommand.Toggle, true)]
    public void IsDefined_AllValidValues_Should_Return_True(PlanSubCommand value, bool expected)
    {
        PlanSubCommandExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== PlanSubCommandConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_EnumValues()
    {
        PlanSubCommandConstants.On.Should().Be("on");
        PlanSubCommandConstants.Enter.Should().Be("enter");
        PlanSubCommandConstants.Off.Should().Be("off");
        PlanSubCommandConstants.Exit.Should().Be("exit");
        PlanSubCommandConstants.Status.Should().Be("status");
        PlanSubCommandConstants.Open.Should().Be("open");
        PlanSubCommandConstants.Toggle.Should().Be("toggle");
    }

    // ===== 往返一致性测试 =====

    [Theory]
    [InlineData(PlanSubCommand.On)]
    [InlineData(PlanSubCommand.Enter)]
    [InlineData(PlanSubCommand.Off)]
    [InlineData(PlanSubCommand.Exit)]
    [InlineData(PlanSubCommand.Status)]
    [InlineData(PlanSubCommand.Open)]
    [InlineData(PlanSubCommand.Toggle)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(PlanSubCommand value)
    {
        var str = value.ToValue();
        PlanSubCommandExtensions.FromValue(str).Should().Be(value);
    }

    // ===== 数量验证 =====

    [Fact]
    public void AllValues_Should_Be_7()
    {
        var values = Enum.GetValues<PlanSubCommand>();
        values.Should().HaveCount(7);
    }
}
