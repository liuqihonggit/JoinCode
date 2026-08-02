namespace Host.Tests.ChatCommands;

/// <summary>
/// McpAction 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / McpActionConstants 常量值
/// 4 个枚举值(status/reconnect/enable/disable)
/// </summary>
public sealed class McpActionExtensionsTests
{
    // ===== ToValue 测试 =====

    [Theory]
    [InlineData(McpAction.Status, "status")]
    [InlineData(McpAction.Reconnect, "reconnect")]
    [InlineData(McpAction.Enable, "enable")]
    [InlineData(McpAction.Disable, "disable")]
    public void ToValue_Should_Return_CorrectString(McpAction value, string expected)
    {
        value.ToValue().Should().Be(expected);
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("status", McpAction.Status)]
    [InlineData("reconnect", McpAction.Reconnect)]
    [InlineData("enable", McpAction.Enable)]
    [InlineData("disable", McpAction.Disable)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, McpAction expected)
    {
        McpActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("STATUS")]
    [InlineData("Reconnect")]
    [InlineData("ENABLE")]
    [InlineData("Disable")]
    public void FromValue_CaseInsensitive_Should_Return_CorrectEnum(string input)
    {
        McpActionExtensions.FromValue(input).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("list")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("enabled")]
    [InlineData("disabled")]
    public void FromValue_InvalidString_Should_Return_Null(string? input)
    {
        McpActionExtensions.FromValue(input).Should().BeNull();
    }

    // ===== RoundTrip 测试 =====

    [Theory]
    [InlineData(McpAction.Status)]
    [InlineData(McpAction.Reconnect)]
    [InlineData(McpAction.Enable)]
    [InlineData(McpAction.Disable)]
    public void RoundTrip_ToValue_ThenFromValue_Should_Return_Same(McpAction value)
    {
        var s = value.ToValue();
        McpActionExtensions.FromValue(s).Should().Be(value);
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(McpAction.Status, true)]
    [InlineData(McpAction.Reconnect, true)]
    [InlineData(McpAction.Enable, true)]
    [InlineData(McpAction.Disable, true)]
    public void IsDefined_KnownValue_Should_Return_True(McpAction value, bool expected)
    {
        McpActionExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== Constants 测试 =====

    [Fact]
    public void Constants_All_Should_Match_EnumValues()
    {
        McpActionConstants.Status.Should().Be("status");
        McpActionConstants.Reconnect.Should().Be("reconnect");
        McpActionConstants.Enable.Should().Be("enable");
        McpActionConstants.Disable.Should().Be("disable");
    }

    // ===== 枚举值数量验证 =====

    [Fact]
    public void AllValues_Should_Be_4()
    {
        var values = Enum.GetValues<McpAction>();
        values.Should().HaveCount(4);
    }

    // ===== 与 CrudAction 边界值不冲突 =====

    [Fact]
    public void FromValue_CrudActionValues_Should_Return_Null()
    {
        // list/create/new/delete/rm/remove 是 CrudAction 范围,不应被 McpAction 解析
        McpActionExtensions.FromValue("list").Should().BeNull();
        McpActionExtensions.FromValue("create").Should().BeNull();
        McpActionExtensions.FromValue("new").Should().BeNull();
        McpActionExtensions.FromValue("delete").Should().BeNull();
        McpActionExtensions.FromValue("rm").Should().BeNull();
        McpActionExtensions.FromValue("remove").Should().BeNull();
    }

    // ===== 与 ToggleAction 边界值验证 =====

    [Fact]
    public void FromValue_EnableDisable_Should_Not_Resolve_ToggleAction()
    {
        // enable/disable 在 McpAction 中表示 MCP 服务器启用/禁用,与 ToggleAction.On/Off 不同
        McpActionExtensions.FromValue("enable").Should().Be(McpAction.Enable);
        McpActionExtensions.FromValue("disable").Should().Be(McpAction.Disable);
    }
}
