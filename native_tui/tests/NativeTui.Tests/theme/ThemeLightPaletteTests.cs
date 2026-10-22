namespace NativeTui.Tests.ThemeTests;

/// <summary>
/// Light 主题全量像素级测试 — 值直接提取自基准 utils/theme.ts lightTheme（115-191 行）。
/// 每键 RGB 硬编码锚点：任何值变更都必须对照基准重新确认（S9 修复回归锁 —
/// 防"Dark with 部分覆盖"式漂移回归：白底终端上深色值不可用）。
/// Dark 主题锚点见 ThemeGoldenTests（本文件零涉及 — Dark 不动）。
/// </summary>
public sealed class ThemeLightPaletteTests
{
    [Fact]
    public void Light_BrandAndModeColors()
    {
        Assert.Equal(Color.FromRgb(215, 119, 87), Theme.Light.Claude);
        Assert.Equal(Color.FromRgb(245, 149, 117), Theme.Light.ClaudeShimmer);
        Assert.Equal(Color.FromRgb(87, 105, 247), Theme.Light.ClaudeBlueForSystemSpinner);
        Assert.Equal(Color.FromRgb(117, 135, 255), Theme.Light.ClaudeBlueShimmerForSystemSpinner);
        Assert.Equal(Color.FromRgb(87, 105, 247), Theme.Light.Permission);
        Assert.Equal(Color.FromRgb(137, 155, 255), Theme.Light.PermissionShimmer);
        Assert.Equal(Color.FromRgb(0, 102, 102), Theme.Light.PlanMode);
        Assert.Equal(Color.FromRgb(135, 0, 255), Theme.Light.AutoAccept);
        Assert.Equal(Color.FromRgb(255, 0, 135), Theme.Light.BashBorder);
        Assert.Equal(Color.FromRgb(71, 130, 200), Theme.Light.Ide);
    }

    [Fact]
    public void Light_PromptBorderColors()
    {
        Assert.Equal(Color.FromRgb(153, 153, 153), Theme.Light.PromptBorder);
        Assert.Equal(Color.FromRgb(183, 183, 183), Theme.Light.PromptBorderShimmer);
    }

    [Fact]
    public void Light_TextColors()
    {
        Assert.Equal(Color.FromRgb(0, 0, 0), Theme.Light.Text);
        Assert.Equal(Color.FromRgb(255, 255, 255), Theme.Light.InverseText);
        Assert.Equal(Color.FromRgb(102, 102, 102), Theme.Light.Inactive);
        Assert.Equal(Color.FromRgb(142, 142, 142), Theme.Light.InactiveShimmer);
        Assert.Equal(Color.FromRgb(175, 175, 175), Theme.Light.Subtle);
        Assert.Equal(Color.FromRgb(87, 105, 247), Theme.Light.Suggestion);
        Assert.Equal(Color.FromRgb(0, 0, 255), Theme.Light.Remember);
    }

    [Fact]
    public void Light_StatusColors()
    {
        Assert.Equal(Color.FromRgb(0, 153, 153), Theme.Light.Background);
        Assert.Equal(Color.FromRgb(44, 122, 57), Theme.Light.Success);
        Assert.Equal(Color.FromRgb(171, 43, 63), Theme.Light.Error);
        Assert.Equal(Color.FromRgb(150, 108, 30), Theme.Light.Warning);
        Assert.Equal(Color.FromRgb(135, 0, 255), Theme.Light.Merged);
        Assert.Equal(Color.FromRgb(200, 158, 80), Theme.Light.WarningShimmer);
    }

    [Fact]
    public void Light_DiffPalette()
    {
        Assert.Equal(Color.FromRgb(105, 219, 124), Theme.Light.DiffAdded);
        Assert.Equal(Color.FromRgb(255, 168, 180), Theme.Light.DiffRemoved);
        Assert.Equal(Color.FromRgb(199, 225, 203), Theme.Light.DiffAddedDimmed);
        Assert.Equal(Color.FromRgb(253, 210, 216), Theme.Light.DiffRemovedDimmed);
        Assert.Equal(Color.FromRgb(47, 157, 68), Theme.Light.DiffAddedWord);
        Assert.Equal(Color.FromRgb(209, 69, 75), Theme.Light.DiffRemovedWord);
    }

    [Fact]
    public void Light_SubagentPalette_Complete8()
    {
        Assert.Equal(Color.FromRgb(220, 38, 38), Theme.Light.RedForSubagentsOnly);
        Assert.Equal(Color.FromRgb(37, 99, 235), Theme.Light.BlueForSubagentsOnly);
        Assert.Equal(Color.FromRgb(22, 163, 74), Theme.Light.GreenForSubagentsOnly);
        Assert.Equal(Color.FromRgb(202, 138, 4), Theme.Light.YellowForSubagentsOnly);
        Assert.Equal(Color.FromRgb(147, 51, 234), Theme.Light.PurpleForSubagentsOnly);
        Assert.Equal(Color.FromRgb(234, 88, 12), Theme.Light.OrangeForSubagentsOnly);
        Assert.Equal(Color.FromRgb(219, 39, 119), Theme.Light.PinkForSubagentsOnly);
        Assert.Equal(Color.FromRgb(8, 145, 178), Theme.Light.CyanForSubagentsOnly);
    }

    [Fact]
    public void Light_MessageBackgrounds()
    {
        Assert.Equal(Color.FromRgb(240, 240, 240), Theme.Light.UserMessageBackground);
        Assert.Equal(Color.FromRgb(252, 252, 252), Theme.Light.UserMessageBackgroundHover);
        Assert.Equal(Color.FromRgb(232, 236, 244), Theme.Light.MessageActionsBackground);
        Assert.Equal(Color.FromRgb(180, 213, 255), Theme.Light.SelectionBg);
        Assert.Equal(Color.FromRgb(250, 245, 250), Theme.Light.BashMessageBackgroundColor);
        Assert.Equal(Color.FromRgb(230, 245, 250), Theme.Light.MemoryBackgroundColor);
    }

    [Fact]
    public void Light_MascotAndMiscColors()
    {
        Assert.Equal(Color.FromRgb(215, 119, 87), Theme.Light.ClawdBody);
        Assert.Equal(Color.FromRgb(0, 0, 0), Theme.Light.ClawdBackground);
        Assert.Equal(Color.FromRgb(106, 155, 204), Theme.Light.ProfessionalBlue);
        Assert.Equal(Color.FromRgb(251, 188, 4), Theme.Light.ChromeYellow);
        Assert.Equal(Color.FromRgb(87, 105, 247), Theme.Light.RateLimitFill);
        Assert.Equal(Color.FromRgb(39, 47, 111), Theme.Light.RateLimitEmpty);
        Assert.Equal(Color.FromRgb(255, 106, 0), Theme.Light.FastMode);
        Assert.Equal(Color.FromRgb(255, 150, 50), Theme.Light.FastModeShimmer);
        Assert.Equal(Color.FromRgb(37, 99, 235), Theme.Light.BriefLabelYou);
        Assert.Equal(Color.FromRgb(215, 119, 87), Theme.Light.BriefLabelClaude);
    }

    [Fact]
    public void Light_RainbowPalette_Complete14()
    {
        Assert.Equal(Color.FromRgb(235, 95, 87), Theme.Light.RainbowRed);
        Assert.Equal(Color.FromRgb(245, 139, 87), Theme.Light.RainbowOrange);
        Assert.Equal(Color.FromRgb(250, 195, 95), Theme.Light.RainbowYellow);
        Assert.Equal(Color.FromRgb(145, 200, 130), Theme.Light.RainbowGreen);
        Assert.Equal(Color.FromRgb(130, 170, 220), Theme.Light.RainbowBlue);
        Assert.Equal(Color.FromRgb(155, 130, 200), Theme.Light.RainbowIndigo);
        Assert.Equal(Color.FromRgb(200, 130, 180), Theme.Light.RainbowViolet);
        Assert.Equal(Color.FromRgb(250, 155, 147), Theme.Light.RainbowRedShimmer);
        Assert.Equal(Color.FromRgb(255, 185, 137), Theme.Light.RainbowOrangeShimmer);
        Assert.Equal(Color.FromRgb(255, 225, 155), Theme.Light.RainbowYellowShimmer);
        Assert.Equal(Color.FromRgb(185, 230, 180), Theme.Light.RainbowGreenShimmer);
        Assert.Equal(Color.FromRgb(180, 205, 240), Theme.Light.RainbowBlueShimmer);
        Assert.Equal(Color.FromRgb(195, 180, 230), Theme.Light.RainbowIndigoShimmer);
        Assert.Equal(Color.FromRgb(230, 180, 210), Theme.Light.RainbowVioletShimmer);
    }

    [Fact]
    public void Light_ExplicitlySourced_NotDerivedFromDark()
    {
        // S9 修复回归锁：曾以 Dark with 部分覆盖，五键静默继承 Dark 错值（任务点名缺口）。
        // 全量显式后这些键必须与 Dark 不同 — 若回归 with 派生并删覆盖，此断言立即失败。
        Assert.NotEqual(Theme.Dark.MessageActionsBackground, Theme.Light.MessageActionsBackground);
        Assert.NotEqual(Theme.Dark.AutoAccept, Theme.Light.AutoAccept);
        Assert.NotEqual(Theme.Dark.Merged, Theme.Light.Merged);
        Assert.NotEqual(Theme.Dark.BashBorder, Theme.Light.BashBorder);
        Assert.NotEqual(Theme.Dark.Background, Theme.Light.Background);
    }

    [Fact]
    public void Light_IndependentInstance_FromDark()
    {
        // 全量显式初始化：Light 是独立实例，改 Dark 任何键不得波及 Light（无 with 派生耦合）
        Assert.NotSame(Theme.Dark, Theme.Light);
    }
}
