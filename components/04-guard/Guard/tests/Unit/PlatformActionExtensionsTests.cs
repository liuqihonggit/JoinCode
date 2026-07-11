namespace Host.Tests.ChatCommands;

/// <summary>
/// PlatformAction 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / PlatformActionConstants 常量值
/// 10 个枚举值(Chrome+Ide+Mobile 3 个命令的 case 字符串 union)
/// </summary>
public sealed class PlatformActionExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_Connect_Should_Return_connect()
    {
        PlatformAction.Connect.ToValue().Should().Be("connect");
    }

    [Fact]
    public void ToValue_Disconnect_Should_Return_disconnect()
    {
        PlatformAction.Disconnect.ToValue().Should().Be("disconnect");
    }

    [Fact]
    public void ToValue_Status_Should_Return_status()
    {
        PlatformAction.Status.ToValue().Should().Be("status");
    }

    [Fact]
    public void ToValue_Install_Should_Return_install()
    {
        PlatformAction.Install.ToValue().Should().Be("install");
    }

    [Fact]
    public void ToValue_Toggle_Should_Return_toggle()
    {
        PlatformAction.Toggle.ToValue().Should().Be("toggle");
    }

    [Fact]
    public void ToValue_Detect_Should_Return_detect()
    {
        PlatformAction.Detect.ToValue().Should().Be("detect");
    }

    [Fact]
    public void ToValue_Open_Should_Return_open()
    {
        PlatformAction.Open.ToValue().Should().Be("open");
    }

    [Fact]
    public void ToValue_Start_Should_Return_start()
    {
        PlatformAction.Start.ToValue().Should().Be("start");
    }

    [Fact]
    public void ToValue_Stop_Should_Return_stop()
    {
        PlatformAction.Stop.ToValue().Should().Be("stop");
    }

    [Fact]
    public void ToValue_Url_Should_Return_url()
    {
        PlatformAction.Url.ToValue().Should().Be("url");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("connect", PlatformAction.Connect)]
    [InlineData("disconnect", PlatformAction.Disconnect)]
    [InlineData("status", PlatformAction.Status)]
    [InlineData("install", PlatformAction.Install)]
    [InlineData("toggle", PlatformAction.Toggle)]
    [InlineData("detect", PlatformAction.Detect)]
    [InlineData("open", PlatformAction.Open)]
    [InlineData("start", PlatformAction.Start)]
    [InlineData("stop", PlatformAction.Stop)]
    [InlineData("url", PlatformAction.Url)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, PlatformAction expected)
    {
        PlatformActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("CONNECT")]
    [InlineData("Disconnect")]
    [InlineData("STATUS")]
    [InlineData("Install")]
    [InlineData("ToGgLe")]
    [InlineData("DETECT")]
    [InlineData("Open")]
    [InlineData("START")]
    [InlineData("Stop")]
    [InlineData("URL")]
    public void FromValue_CaseInsensitive_Should_Return_CorrectEnum(string input)
    {
        PlatformActionExtensions.FromValue(input).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("list")]
    [InlineData("connect2")]
    [InlineData("disconnected")]
    [InlineData("openfile")]
    [InlineData("started")]
    public void FromValue_InvalidString_Should_Return_Null(string? input)
    {
        PlatformActionExtensions.FromValue(input).Should().BeNull();
    }

    // ===== RoundTrip 测试 =====

    [Theory]
    [InlineData(PlatformAction.Connect)]
    [InlineData(PlatformAction.Disconnect)]
    [InlineData(PlatformAction.Status)]
    [InlineData(PlatformAction.Install)]
    [InlineData(PlatformAction.Toggle)]
    [InlineData(PlatformAction.Detect)]
    [InlineData(PlatformAction.Open)]
    [InlineData(PlatformAction.Start)]
    [InlineData(PlatformAction.Stop)]
    [InlineData(PlatformAction.Url)]
    public void RoundTrip_ToValue_ThenFromValue_Should_Return_Same(PlatformAction value)
    {
        var s = value.ToValue();
        PlatformActionExtensions.FromValue(s).Should().Be(value);
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(PlatformAction.Connect, true)]
    [InlineData(PlatformAction.Disconnect, true)]
    [InlineData(PlatformAction.Status, true)]
    [InlineData(PlatformAction.Install, true)]
    [InlineData(PlatformAction.Toggle, true)]
    [InlineData(PlatformAction.Detect, true)]
    [InlineData(PlatformAction.Open, true)]
    [InlineData(PlatformAction.Start, true)]
    [InlineData(PlatformAction.Stop, true)]
    [InlineData(PlatformAction.Url, true)]
    public void IsDefined_KnownValue_Should_Return_True(PlatformAction value, bool expected)
    {
        PlatformActionExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== Constants 测试 =====

    [Fact]
    public void Constants_Connect_Should_Be_connect()
    {
        PlatformActionConstants.Connect.Should().Be("connect");
    }

    [Fact]
    public void Constants_Disconnect_Should_Be_disconnect()
    {
        PlatformActionConstants.Disconnect.Should().Be("disconnect");
    }

    [Fact]
    public void Constants_Status_Should_Be_status()
    {
        PlatformActionConstants.Status.Should().Be("status");
    }

    [Fact]
    public void Constants_Install_Should_Be_install()
    {
        PlatformActionConstants.Install.Should().Be("install");
    }

    [Fact]
    public void Constants_Toggle_Should_Be_toggle()
    {
        PlatformActionConstants.Toggle.Should().Be("toggle");
    }

    [Fact]
    public void Constants_Detect_Should_Be_detect()
    {
        PlatformActionConstants.Detect.Should().Be("detect");
    }

    [Fact]
    public void Constants_Open_Should_Be_open()
    {
        PlatformActionConstants.Open.Should().Be("open");
    }

    [Fact]
    public void Constants_Start_Should_Be_start()
    {
        PlatformActionConstants.Start.Should().Be("start");
    }

    [Fact]
    public void Constants_Stop_Should_Be_stop()
    {
        PlatformActionConstants.Stop.Should().Be("stop");
    }

    [Fact]
    public void Constants_Url_Should_Be_url()
    {
        PlatformActionConstants.Url.Should().Be("url");
    }

    // ===== 枚举值数量验证 =====

    [Fact]
    public void AllValues_Should_Be_10()
    {
        // Chrome(5) + Ide(5) + Mobile(3) - 共享 3 个(connect/disconnect/status) = 10
        var values = Enum.GetValues<PlatformAction>();
        values.Should().HaveCount(10);
    }

    // ===== 跨命令共享值验证 =====

    [Fact]
    public void FromValue_SharedActions_Should_Resolve_Same_Enum()
    {
        // connect/disconnect/status 是 chrome+ide 共享的,3 个命令都应解析到同一枚举值
        PlatformActionExtensions.FromValue("connect").Should().Be(PlatformAction.Connect);
        PlatformActionExtensions.FromValue("disconnect").Should().Be(PlatformAction.Disconnect);
        PlatformActionExtensions.FromValue("status").Should().Be(PlatformAction.Status);
    }

    [Fact]
    public void FromValue_ChromeOnlyActions_Should_Resolve()
    {
        // install/toggle 是 chrome 专属
        PlatformActionExtensions.FromValue("install").Should().Be(PlatformAction.Install);
        PlatformActionExtensions.FromValue("toggle").Should().Be(PlatformAction.Toggle);
    }

    [Fact]
    public void FromValue_IdeOnlyActions_Should_Resolve()
    {
        // detect/open 是 ide 专属
        PlatformActionExtensions.FromValue("detect").Should().Be(PlatformAction.Detect);
        PlatformActionExtensions.FromValue("open").Should().Be(PlatformAction.Open);
    }

    [Fact]
    public void FromValue_MobileOnlyActions_Should_Resolve()
    {
        // start/stop/url 是 mobile 专属
        PlatformActionExtensions.FromValue("start").Should().Be(PlatformAction.Start);
        PlatformActionExtensions.FromValue("stop").Should().Be(PlatformAction.Stop);
        PlatformActionExtensions.FromValue("url").Should().Be(PlatformAction.Url);
    }
}
