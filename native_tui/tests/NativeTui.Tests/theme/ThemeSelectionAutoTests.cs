namespace NativeTui.Tests;

/// <summary>
/// 主题选择 auto 解析测试（S11 遗留#3 双通道裁决落地）— auto 为唯一数据源收编：
/// ①清单含 auto（对齐引擎侧 ThemeCommand 七名 — 双通道主题名集合一致）；
/// ②ResolveAutoName 按小时解析（6-18 light 其余 dark — 引擎 ApplyTheme 同规则）；
/// ③Resolve(auto) 走当前时段（小时注入重载确定性验证）；
/// ④持久化写原名（auto 字面量 — 与引擎侧 SetAsync("theme","auto") 语义一致，非解析值）。
/// </summary>
public sealed class ThemeSelectionAutoTests
{
    [Theory]
    [InlineData(6, ThemeSelection.LightName)]    // 晨 6 点边界含
    [InlineData(12, ThemeSelection.LightName)]
    [InlineData(17, ThemeSelection.LightName)]   // 午后 17 点含
    [InlineData(18, ThemeSelection.DarkName)]    // 晚 18 点边界不含
    [InlineData(0, ThemeSelection.DarkName)]
    [InlineData(5, ThemeSelection.DarkName)]
    [InlineData(23, ThemeSelection.DarkName)]
    public void ResolveAutoName_ByHour(int hour, string expected) =>
        Assert.Equal(expected, ThemeSelection.ResolveAutoName(hour));

    [Fact]
    public void Names_IncludeAuto_First() =>
        Assert.Equal(ThemeSelection.AutoName, ThemeSelection.Names[0]);

    [Fact]
    public void Names_Count_Seven_EngineAligned() =>
        Assert.Equal(7, ThemeSelection.Names.Count);

    [Theory]
    [InlineData(12, true)]    // 白天 auto → Light 色板
    [InlineData(20, false)]   // 夜间 auto → Dark 色板
    public void Resolve_Auto_ResolvesByHour(int hour, bool expectLight)
    {
        var theme = ThemeSelection.Resolve(ThemeSelection.AutoName, hour);
        Assert.Equal(expectLight, ReferenceEquals(theme, Theme.Light));
    }

    [Fact]
    public void Resolve_DefaultHourOverload_Auto_DoesNotThrow()
    {
        // 无小时重载走 DateTime.Now.Hour — 冒烟（不断言具体色板，时段不定）
        var theme = ThemeSelection.Resolve(ThemeSelection.AutoName);
        Assert.True(ReferenceEquals(theme, Theme.Light) || ReferenceEquals(theme, Theme.Dark));
    }
}
