namespace NativeTui.Tests.Repl;

/// <summary>
/// Spinner 行渲染 L1 golden 测试 — 像素级对齐基准 SpinnerGlyph.tsx + GlimmerMessage.tsx：
/// glyph 占 2 列（帧字符 + 空格）、停滞红 RGB 插值、reducedMotion ● 半秒周期 dim、
/// shimmer 三段切分（before/shim/after + 尾随空格）、tool-use 整体 flash。
/// 期望色值由基准 interpolateColor 逐通道推演。
/// </summary>
public sealed class SpinnerRowTests
{
    private static SpinnerAnimState State(int frame = 0, int glimmer = SpinnerMath.GlimmerOff, double stalled = 0, double flash = 0) => new()
    {
        Frame = frame,
        GlimmerIndex = glimmer,
        StalledIntensity = stalled,
        FlashOpacity = flash,
    };

    [Fact]
    public void Glyph_TwoCols_FrameCharPlusSpace()
    {
        using var screen = new Screen(20, 1);
        SpinnerRow.RenderGlyph(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, State(frame: 0), reducedMotion: false, timeMs: 0);
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Claude });
        Assert.Equal(new Rune('·'), screen[0, 0].Value);
        Assert.Equal(style, screen[0, 0].StyleId);
        // Box width=2：col 1 为尾随空格（Blank）
        Assert.Equal(Cell.Blank, screen[0, 1]);
        // frame 3 → ✶；frame 12 回绕 → ·
        SpinnerRow.RenderGlyph(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, State(frame: 3), false, 0);
        Assert.Equal(new Rune('✶'), screen[0, 0].Value);
        SpinnerRow.RenderGlyph(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, State(frame: 12), false, 0);
        Assert.Equal(new Rune('·'), screen[0, 0].Value);
    }

    [Fact]
    public void Glyph_Stalled_InterpolatesTowardErrorRed()
    {
        using var screen = new Screen(20, 1);
        // claude(215,119,87) → ERROR_RED(171,43,63) t=0.5：(193,81,75)
        SpinnerRow.RenderGlyph(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, State(stalled: 0.5), false, 0);
        var red = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(193, 81, 75) });
        Assert.Equal(red, screen[0, 0].StyleId);
    }

    [Fact]
    public void Glyph_ReducedMotion_DotWith1sDimCycle()
    {
        using var screen = new Screen(20, 1);
        var bright = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Claude });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Claude, Dim = true });
        SpinnerRow.RenderGlyph(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, State(), reducedMotion: true, timeMs: 0);
        Assert.Equal(new Rune('●'), screen[0, 0].Value);
        Assert.Equal(bright, screen[0, 0].StyleId);
        // floor(1000/1000)%2=1 → dim 半周期
        SpinnerRow.RenderGlyph(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, State(), true, 1000);
        Assert.Equal(dim, screen[0, 0].StyleId);
        SpinnerRow.RenderGlyph(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, State(), true, 2000);
        Assert.Equal(bright, screen[0, 0].StyleId);
    }

    [Fact]
    public void Glimmer_EmptyMessage_RendersNothing()
    {
        using var screen = new Screen(20, 1);
        SpinnerRow.RenderGlimmer(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, string.Empty, SpinnerMode.Responding, State());
        Assert.Equal(Cell.Blank, screen[0, 0]);
        Assert.Equal(Cell.Blank, screen[0, 19]);
    }

    [Fact]
    public void Glimmer_WindowOutside_PlainMessagePlusTrailingSpace()
    {
        using var screen = new Screen(20, 1);
        SpinnerRow.RenderGlimmer(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, "hi", SpinnerMode.Responding, State(glimmer: -100));
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Claude });
        Assert.Equal("hi ", RowText.Of(screen, 0, 0, 3));
        Assert.Equal(style, screen[0, 0].StyleId);
        Assert.Equal(style, screen[0, 1].StyleId);
        // 尾随空格同为 messageColor（基准 <Text color={messageColor}> </Text>）
        Assert.Equal(style, screen[0, 2].StyleId);
    }

    [Fact]
    public void Glimmer_ShimmerWindow_SplitsThreeSpans()
    {
        using var screen = new Screen(20, 1);
        // "hello" glimmerIndex=2 → 窗口 [1,3]：h | ell(高亮) | o
        SpinnerRow.RenderGlimmer(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, "hello", SpinnerMode.Responding, State(glimmer: 2));
        var msg = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Claude });
        var shim = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.ClaudeShimmer });
        Assert.Equal(msg, screen[0, 0].StyleId);
        Assert.Equal(shim, screen[0, 1].StyleId);
        Assert.Equal(shim, screen[0, 2].StyleId);
        Assert.Equal(shim, screen[0, 3].StyleId);
        Assert.Equal(msg, screen[0, 4].StyleId);
        Assert.Equal(msg, screen[0, 5].StyleId);
        Assert.Equal("hello ", RowText.Of(screen, 0, 0, 6));
    }

    [Fact]
    public void Glimmer_NegativeWindow_ClampsStartToZero()
    {
        using var screen = new Screen(20, 1);
        // glimmerIndex=0 → 窗口 [-1,1] → clampedStart=0：无 before，前 2 字符高亮
        SpinnerRow.RenderGlimmer(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, "hello", SpinnerMode.Responding, State(glimmer: 0));
        var msg = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Claude });
        var shim = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.ClaudeShimmer });
        Assert.Equal(shim, screen[0, 0].StyleId);
        Assert.Equal(shim, screen[0, 1].StyleId);
        Assert.Equal(msg, screen[0, 2].StyleId);
        Assert.Equal(msg, screen[0, 3].StyleId);
        Assert.Equal(msg, screen[0, 4].StyleId);
    }

    [Fact]
    public void Glimmer_Stalled_WholeMessageAndSpaceInRed()
    {
        using var screen = new Screen(20, 1);
        SpinnerRow.RenderGlimmer(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, "hi", SpinnerMode.Responding, State(stalled: 0.5));
        var red = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(193, 81, 75) });
        Assert.Equal(red, screen[0, 0].StyleId);
        Assert.Equal(red, screen[0, 1].StyleId);
        // 停滞路径尾随空格也是红（基准 stalled 分支 message + ' ' 同色）
        Assert.Equal(red, screen[0, 2].StyleId);
    }

    [Fact]
    public void Glimmer_ToolUse_FlashInterpolatesWholeMessage()
    {
        using var screen = new Screen(20, 1);
        // flash 0.5：claude(215,119,87) ↔ claudeShimmer(235,159,127) → (225,139,107)
        SpinnerRow.RenderGlimmer(screen, new Rect(0, 0, 20, 1), Theme.Dark, Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, "hi", SpinnerMode.ToolUse, State(flash: 0.5));
        var flash = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(225, 139, 107) });
        var msg = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Claude });
        Assert.Equal(flash, screen[0, 0].StyleId);
        Assert.Equal(flash, screen[0, 1].StyleId);
        // flash 路径尾随空格回到 messageColor
        Assert.Equal(msg, screen[0, 2].StyleId);
    }

    [Fact]
    public void Row_ComposesGlyphThenGlimmer()
    {
        using var screen = new Screen(20, 1);
        SpinnerRow.Render(screen, new Rect(0, 0, 20, 1), Theme.Dark, "hi", Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, SpinnerMode.Responding, State(), reducedMotion: false, timeMs: 0);
        Assert.Equal("· hi ", RowText.Of(screen, 0, 0, 5));
        // col 1 为 glyph Box 尾随空格（Blank），message 从 col 2 起
        Assert.Equal(Cell.Blank, screen[0, 1]);
        Assert.Equal(new Rune('h'), screen[0, 2].Value);
    }

    [Fact]
    public void Row_TruncatesToFrame()
    {
        using var screen = new Screen(20, 1);
        SpinnerRow.Render(screen, new Rect(0, 0, 4, 1), Theme.Dark, "hi", Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, SpinnerMode.Responding, State(), false, 0);
        Assert.Equal("· hi", RowText.Of(screen, 0, 0, 4));
        Assert.Equal(Cell.Blank, screen[0, 4]);
    }

    [Fact]
    public void Row_EmptyMessage_GlyphOnly()
    {
        using var screen = new Screen(20, 1);
        SpinnerRow.Render(screen, new Rect(0, 0, 20, 1), Theme.Dark, string.Empty, Theme.Dark.Claude, Theme.Dark.ClaudeShimmer, SpinnerMode.Responding, State(), false, 0);
        Assert.Equal(new Rune('·'), screen[0, 0].Value);
        Assert.Equal(Cell.Blank, screen[0, 2]);
    }
}
