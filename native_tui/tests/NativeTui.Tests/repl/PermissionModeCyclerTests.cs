namespace NativeTui.Tests.Repl;

/// <summary>
/// 权限模式测试 — jcc 引擎模式集合（plan/auto/ask/bypass/unattended）：
/// 循环链 auto → plan → ask → bypass → unattended → auto（bypass 按可用性短路），
/// 协议字符串值与引擎 [EnumValue] 一致，中文标题汉化，未知值回 auto。
/// </summary>
public sealed class PermissionModeCyclerTests
{
    [Theory]
    [InlineData(PermissionMode.Auto, PermissionMode.Plan)]
    [InlineData(PermissionMode.Plan, PermissionMode.Ask)]
    [InlineData(PermissionMode.Bypass, PermissionMode.Unattended)]
    [InlineData(PermissionMode.Unattended, PermissionMode.Auto)]
    public void Next_FollowsEngineCarousel(PermissionMode current, PermissionMode expected)
    {
        Assert.Equal(expected, PermissionModeCycler.Next(current, bypassAvailable: true));
    }

    [Fact]
    public void Ask_WithBypass_CyclesToBypass()
    {
        Assert.Equal(PermissionMode.Bypass, PermissionModeCycler.Next(PermissionMode.Ask, bypassAvailable: true));
    }

    [Fact]
    public void Ask_WithoutBypass_SkipsToUnattended()
    {
        Assert.Equal(PermissionMode.Unattended, PermissionModeCycler.Next(PermissionMode.Ask, bypassAvailable: false));
    }

    [Fact]
    public void FullCycle_TraversesAllEngineModes()
    {
        var visited = new List<PermissionMode>();
        var mode = PermissionMode.Auto;
        for (var i = 0; i < 5; i++)
        {
            mode = PermissionModeCycler.Next(mode, bypassAvailable: true);
            visited.Add(mode);
        }

        Assert.Equal(
            [PermissionMode.Plan, PermissionMode.Ask, PermissionMode.Bypass, PermissionMode.Unattended, PermissionMode.Auto],
            visited);
    }

    [Theory]
    [InlineData(PermissionMode.Plan, "plan")]
    [InlineData(PermissionMode.Auto, "auto")]
    [InlineData(PermissionMode.Ask, "ask")]
    [InlineData(PermissionMode.Bypass, "bypass")]
    [InlineData(PermissionMode.Unattended, "unattended")]
    public void ToValue_MatchesEngineEnumValues(PermissionMode mode, string expected)
    {
        Assert.Equal(expected, PermissionModeInfo.ToValue(mode));
    }

    [Theory]
    [InlineData("plan", PermissionMode.Plan)]
    [InlineData("auto", PermissionMode.Auto)]
    [InlineData("ask", PermissionMode.Ask)]
    [InlineData("bypass", PermissionMode.Bypass)]
    [InlineData("unattended", PermissionMode.Unattended)]
    public void TryFromValue_ParsesEngineValues(string value, PermissionMode expected)
    {
        Assert.True(PermissionModeInfo.TryFromValue(value, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("acceptEdits")]
    [InlineData("bypassPermissions")]
    [InlineData("dontAsk")]
    public void TryFromValue_RejectsLegacyAndUnknownValues(string? value)
    {
        Assert.False(PermissionModeInfo.TryFromValue(value, out var mode));
        Assert.Equal(PermissionMode.Auto, mode);
    }

    [Theory]
    [InlineData(PermissionMode.Plan, "计划模式", "⏸")]
    [InlineData(PermissionMode.Auto, "标准模式", "")]
    [InlineData(PermissionMode.Ask, "询问模式", "⏵⏵")]
    [InlineData(PermissionMode.Bypass, "全放行", "⏵⏵")]
    [InlineData(PermissionMode.Unattended, "无人值守", "⏵⏵")]
    public void Info_ChineseTitleAndSymbol(PermissionMode mode, string title, string symbol)
    {
        Assert.Equal(title, PermissionModeInfo.Title(mode));
        Assert.Equal(symbol, PermissionModeInfo.Symbol(mode));
    }
}
