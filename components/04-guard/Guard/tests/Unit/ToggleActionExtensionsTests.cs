namespace Host.Tests.ChatCommands;

/// <summary>
/// ToggleAction 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / ToggleActionConstants 常量值
/// </summary>
public sealed class ToggleActionExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_On_Should_Return_on()
    {
        ToggleAction.On.ToValue().Should().Be("on");
    }

    [Fact]
    public void ToValue_Off_Should_Return_off()
    {
        ToggleAction.Off.ToValue().Should().Be("off");
    }

    [Fact]
    public void ToValue_Status_Should_Return_status()
    {
        ToggleAction.Status.ToValue().Should().Be("status");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("on", ToggleAction.On)]
    [InlineData("off", ToggleAction.Off)]
    [InlineData("status", ToggleAction.Status)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, ToggleAction expected)
    {
        ToggleActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void FromValue_Should_Be_CaseInsensitive()
    {
        ToggleActionExtensions.FromValue("ON").Should().Be(ToggleAction.On);
        ToggleActionExtensions.FromValue("Off").Should().Be(ToggleAction.Off);
        ToggleActionExtensions.FromValue("STATUS").Should().Be(ToggleAction.Status);
    }

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        ToggleActionExtensions.FromValue("invalid").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        ToggleActionExtensions.FromValue("").Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        ToggleActionExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(ToggleAction.On, true)]
    [InlineData(ToggleAction.Off, true)]
    [InlineData(ToggleAction.Status, true)]
    public void IsDefined_AllValidValues_Should_Return_True(ToggleAction value, bool expected)
    {
        ToggleActionExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== ToggleActionConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_EnumValues()
    {
        ToggleActionConstants.On.Should().Be("on");
        ToggleActionConstants.Off.Should().Be("off");
        ToggleActionConstants.Status.Should().Be("status");
    }

    // ===== 往返一致性测试 =====

    [Theory]
    [InlineData(ToggleAction.On)]
    [InlineData(ToggleAction.Off)]
    [InlineData(ToggleAction.Status)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(ToggleAction value)
    {
        var str = value.ToValue();
        ToggleActionExtensions.FromValue(str).Should().Be(value);
    }
}
