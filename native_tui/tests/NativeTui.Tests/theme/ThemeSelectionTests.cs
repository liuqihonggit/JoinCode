namespace NativeTui.Tests.ThemeTests;

/// <summary>
/// 主题选择测试 — 对齐基准 theme.ts THEME_NAMES 清单序 + getTheme 解析语义：
/// 名称精确匹配、无效名回退 Dark；daltonized/ansi 四名 P3 未实现暂回退 Dark。
/// Assert.Same 验证静态单例身份（Resolve 零分配 — 不 new 色板）。
/// </summary>
public sealed class ThemeSelectionTests
{
    [Fact]
    public void Names_SixEntries_BaselineOrder()
    {
        string[] expected =
        [
            "dark",
            "light",
            "light-daltonized",
            "dark-daltonized",
            "light-ansi",
            "dark-ansi",
        ];
        Assert.Equal(expected, ThemeSelection.Names);
    }

    [Fact]
    public void Names_MatchNameConstants()
    {
        // 名称常量与清单同源锁定（防清单与常量两套硬编码漂移）
        Assert.Equal(6, ThemeSelection.Names.Count);
        Assert.Contains(ThemeSelection.DarkName, ThemeSelection.Names);
        Assert.Contains(ThemeSelection.LightName, ThemeSelection.Names);
        Assert.Contains(ThemeSelection.LightDaltonizedName, ThemeSelection.Names);
        Assert.Contains(ThemeSelection.DarkDaltonizedName, ThemeSelection.Names);
        Assert.Contains(ThemeSelection.LightAnsiName, ThemeSelection.Names);
        Assert.Contains(ThemeSelection.DarkAnsiName, ThemeSelection.Names);
    }

    [Fact]
    public void Resolve_Dark_ReturnsDarkSingleton()
    {
        Assert.Same(Theme.Dark, ThemeSelection.Resolve(ThemeSelection.DarkName));
    }

    [Fact]
    public void Resolve_Light_ReturnsLightSingleton()
    {
        Assert.Same(Theme.Light, ThemeSelection.Resolve(ThemeSelection.LightName));
    }

    [Fact]
    public void Resolve_DaltonizedAndAnsi_P3FallbackToDark()
    {
        // P3：色板未实现暂回退 Dark（落地后此测试改为断言各自色板）
        Assert.Same(Theme.Dark, ThemeSelection.Resolve(ThemeSelection.LightDaltonizedName));
        Assert.Same(Theme.Dark, ThemeSelection.Resolve(ThemeSelection.DarkDaltonizedName));
        Assert.Same(Theme.Dark, ThemeSelection.Resolve(ThemeSelection.LightAnsiName));
        Assert.Same(Theme.Dark, ThemeSelection.Resolve(ThemeSelection.DarkAnsiName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("solarized")]
    [InlineData("Dark")]
    [InlineData("LIGHT")]
    public void Resolve_UnknownOrNull_FallsBackToDark(string? name)
    {
        // 基准 getTheme 精确 switch：大小写不匹配落 default → Dark
        Assert.Same(Theme.Dark, ThemeSelection.Resolve(name));
    }

    [Fact]
    public void Resolve_EveryListedName_NeverReturnsNull()
    {
        foreach (var name in ThemeSelection.Names)
        {
            Assert.NotNull(ThemeSelection.Resolve(name));
        }
    }
}
