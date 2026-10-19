namespace NativeTui.Tests.ThemeTests;

/// <summary>
/// 主题与符号表 golden 测试 — 值直接提取自基准源码（像素级对齐的数据锚点）。
/// 任何值变更都必须对照基准 theme.ts / figures.ts 重新确认。
/// </summary>
public sealed class ThemeGoldenTests
{
    [Fact]
    public void Dark_ClaudeOrange()
    {
        Assert.Equal(Color.FromRgb(215, 119, 87), Theme.Dark.Claude);
        Assert.Equal(Color.FromRgb(235, 159, 127), Theme.Dark.ClaudeShimmer);
    }

    [Fact]
    public void Dark_UserMessageBackground()
    {
        Assert.Equal(Color.FromRgb(55, 55, 55), Theme.Dark.UserMessageBackground);
    }

    [Fact]
    public void Light_UserMessageBackground()
    {
        Assert.Equal(Color.FromRgb(240, 240, 240), Theme.Light.UserMessageBackground);
    }

    [Fact]
    public void Dark_PromptBorder()
    {
        Assert.Equal(Color.FromRgb(136, 136, 136), Theme.Dark.PromptBorder);
    }

    [Fact]
    public void Dark_SelectionBg()
    {
        Assert.Equal(Color.FromRgb(38, 79, 120), Theme.Dark.SelectionBg);
    }

    [Fact]
    public void Dark_DiffPalette()
    {
        Assert.Equal(Color.FromRgb(34, 92, 43), Theme.Dark.DiffAdded);
        Assert.Equal(Color.FromRgb(122, 41, 54), Theme.Dark.DiffRemoved);
        Assert.Equal(Color.FromRgb(56, 166, 96), Theme.Dark.DiffAddedWord);
        Assert.Equal(Color.FromRgb(179, 89, 107), Theme.Dark.DiffRemovedWord);
    }

    [Fact]
    public void Dark_ModeColors()
    {
        Assert.Equal(Color.FromRgb(175, 135, 255), Theme.Dark.AutoAccept);
        Assert.Equal(Color.FromRgb(72, 150, 140), Theme.Dark.PlanMode);
        Assert.Equal(Color.FromRgb(177, 185, 249), Theme.Dark.Permission);
        Assert.Equal(Color.FromRgb(253, 93, 177), Theme.Dark.BashBorder);
    }

    [Fact]
    public void Dark_StatusColors()
    {
        Assert.Equal(Color.FromRgb(78, 186, 101), Theme.Dark.Success);
        Assert.Equal(Color.FromRgb(255, 107, 128), Theme.Dark.Error);
        Assert.Equal(Color.FromRgb(255, 193, 7), Theme.Dark.Warning);
    }

    [Fact]
    public void Dark_SubagentPalette_Complete8()
    {
        Assert.Equal(Color.FromRgb(220, 38, 38), Theme.Dark.RedForSubagentsOnly);
        Assert.Equal(Color.FromRgb(37, 99, 235), Theme.Dark.BlueForSubagentsOnly);
        Assert.Equal(Color.FromRgb(22, 163, 74), Theme.Dark.GreenForSubagentsOnly);
        Assert.Equal(Color.FromRgb(202, 138, 4), Theme.Dark.YellowForSubagentsOnly);
        Assert.Equal(Color.FromRgb(147, 51, 234), Theme.Dark.PurpleForSubagentsOnly);
        Assert.Equal(Color.FromRgb(234, 88, 12), Theme.Dark.OrangeForSubagentsOnly);
        Assert.Equal(Color.FromRgb(219, 39, 119), Theme.Dark.PinkForSubagentsOnly);
        Assert.Equal(Color.FromRgb(8, 145, 178), Theme.Dark.CyanForSubagentsOnly);
    }

    [Fact]
    public void Light_PromptBorder()
    {
        Assert.Equal(Color.FromRgb(153, 153, 153), Theme.Light.PromptBorder);
    }

    [Fact]
    public void Light_SelectionBg()
    {
        Assert.Equal(Color.FromRgb(180, 213, 255), Theme.Light.SelectionBg);
    }
}

/// <summary>
/// 符号常量表 golden 测试 — 对齐基准 constants/figures.ts。
/// </summary>
public sealed class FiguresGoldenTests
{
    [Fact]
    public void MessageBullet_NonDarwin()
    {
        Assert.Equal("●", Figures.MessageBullet);
        Assert.Equal("⏺", Figures.BlackCircleDarwin);
    }

    [Fact]
    public void ToolResultIndent_ExactFiveChars()
    {
        Assert.Equal("  ⎿  ", Figures.ToolResultIndent);
        Assert.Equal(5, Figures.ToolResultIndent.Length);
    }

    [Fact]
    public void Pointer_IsRightAngleBracket()
    {
        Assert.Equal("❯", Figures.Pointer);
    }

    [Theory]
    [InlineData(nameof(Figures.BulletOperator), "∙")]
    [InlineData(nameof(Figures.TeardropAsterisk), "✻")]
    [InlineData(nameof(Figures.UpArrow), "↑")]
    [InlineData(nameof(Figures.DownArrow), "↓")]
    [InlineData(nameof(Figures.LightningBolt), "↯")]
    [InlineData(nameof(Figures.EffortLow), "○")]
    [InlineData(nameof(Figures.EffortMedium), "◐")]
    [InlineData(nameof(Figures.EffortMax), "◉")]
    [InlineData(nameof(Figures.BlockquoteBar), "▎")]
    [InlineData(nameof(Figures.HeavyHorizontal), "━")]
    [InlineData(nameof(Figures.BridgeReadyIndicator), "·✔︎·")]
    [InlineData(nameof(Figures.BridgeFailedIndicator), "×")]
    public void FigureValues_MatchBaseline(string fieldName, string expected)
    {
        var actual = typeof(Figures).GetField(fieldName)!.GetValue(null);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BridgeSpinnerFrames_FourFrames()
    {
        Assert.Equal(4, Figures.BridgeSpinnerFrames.Length);
        Assert.Equal("·|·", Figures.BridgeSpinnerFrames[0]);
    }
}
