namespace Host.Tests.ChatCommands;

/// <summary>
/// BridgeAction 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / BridgeActionConstants 常量值
/// 5 个枚举值(qr/sessions/status/connect/disconnect)
/// </summary>
public sealed class BridgeActionExtensionsTests
{
    // ===== ToValue 测试 =====

    [Theory]
    [InlineData(BridgeAction.Qr, "qr")]
    [InlineData(BridgeAction.Sessions, "sessions")]
    [InlineData(BridgeAction.Status, "status")]
    [InlineData(BridgeAction.Connect, "connect")]
    [InlineData(BridgeAction.Disconnect, "disconnect")]
    public void ToValue_Should_Return_CorrectString(BridgeAction value, string expected)
    {
        value.ToValue().Should().Be(expected);
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("qr", BridgeAction.Qr)]
    [InlineData("sessions", BridgeAction.Sessions)]
    [InlineData("status", BridgeAction.Status)]
    [InlineData("connect", BridgeAction.Connect)]
    [InlineData("disconnect", BridgeAction.Disconnect)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, BridgeAction expected)
    {
        BridgeActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("QR")]
    [InlineData("SESSIONS")]
    [InlineData("Status")]
    [InlineData("CONNECT")]
    [InlineData("Disconnect")]
    public void FromValue_CaseInsensitive_Should_Return_CorrectEnum(string input)
    {
        BridgeActionExtensions.FromValue(input).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("list")]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("connected")]
    public void FromValue_InvalidString_Should_Return_Null(string? input)
    {
        BridgeActionExtensions.FromValue(input).Should().BeNull();
    }

    // ===== RoundTrip 测试 =====

    [Theory]
    [InlineData(BridgeAction.Qr)]
    [InlineData(BridgeAction.Sessions)]
    [InlineData(BridgeAction.Status)]
    [InlineData(BridgeAction.Connect)]
    [InlineData(BridgeAction.Disconnect)]
    public void RoundTrip_ToValue_ThenFromValue_Should_Return_Same(BridgeAction value)
    {
        var s = value.ToValue();
        BridgeActionExtensions.FromValue(s).Should().Be(value);
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(BridgeAction.Qr, true)]
    [InlineData(BridgeAction.Sessions, true)]
    [InlineData(BridgeAction.Status, true)]
    [InlineData(BridgeAction.Connect, true)]
    [InlineData(BridgeAction.Disconnect, true)]
    public void IsDefined_KnownValue_Should_Return_True(BridgeAction value, bool expected)
    {
        BridgeActionExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== Constants 测试 =====

    [Fact]
    public void Constants_All_Should_Match_EnumValues()
    {
        BridgeActionConstants.Qr.Should().Be("qr");
        BridgeActionConstants.Sessions.Should().Be("sessions");
        BridgeActionConstants.Status.Should().Be("status");
        BridgeActionConstants.Connect.Should().Be("connect");
        BridgeActionConstants.Disconnect.Should().Be("disconnect");
    }

    // ===== 枚举值数量验证 =====

    [Fact]
    public void AllValues_Should_Be_5()
    {
        var values = Enum.GetValues<BridgeAction>();
        values.Should().HaveCount(5);
    }

    // ===== 与 PlatformAction 边界值验证 =====

    [Fact]
    public void FromValue_ConnectDisconnect_Should_Not_Resolve_PlatformAction()
    {
        // connect/disconnect 在 PlatformAction 和 BridgeAction 中都存在,但分属不同枚举
        // BridgeAction 独立管理 Bridge 子系统语义,不依赖 PlatformAction
        BridgeActionExtensions.FromValue("connect").Should().Be(BridgeAction.Connect);
        BridgeActionExtensions.FromValue("disconnect").Should().Be(BridgeAction.Disconnect);
    }
}
