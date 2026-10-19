namespace NativeTui.Tests.Repl;

/// <summary>
/// 启动 banner golden 测试 — 像素级对齐基准 CondensedLogo + Clawd（default pose）：
/// logo 三行 9 列 Box（row0 " ▐▛███▜▌" / row1 "▝▜█████▛▘" / row2 "  ▘▘ ▝▝  "，
/// clawd_body 前景；row0/row1 中段 5 列叠加 clawd_background 背景）
/// + gap 2 + 右列三行（bold "jcc" + 默认空格 + dim "v{version}" / dim 模型·计费 / dim cwd）。
/// </summary>
public sealed class BannerItemTests
{
    [Fact]
    public void Render_ClawdArtAndInfoColumn()
    {
        using var screen = new Screen(cols: 60, rows: 3);
        var banner = new BannerItem("2.1.303", "glm-4.7", "API 按量计费", @"C:\Users\54076");
        banner.Render(screen, new Rect(0, 0, 60, 3), Theme.Dark);

        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.ClawdBody });
        var bodyOnBg = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.ClawdBody, Background = Theme.Dark.ClawdBackground });

        // logo 三行（default pose；row0 内容 8 列靠左，Box 宽 9）
        Assert.Equal(" ▐▛███▜▌", RowText.Of(screen, 0, 0, 8));
        Assert.Equal("▝▜█████▛▘", RowText.Of(screen, 1, 0, 9));
        Assert.Equal("  ▘▘ ▝▝  ", RowText.Of(screen, 2, 0, 9));
        // 边段仅前景，中段前景 + clawd_background 背景
        Assert.Equal(body, screen[0, 1].StyleId);
        Assert.Equal(body, screen[0, 7].StyleId);
        Assert.Equal(bodyOnBg, screen[0, 2].StyleId);
        Assert.Equal(body, screen[1, 0].StyleId);
        Assert.Equal(bodyOnBg, screen[1, 2].StyleId);
        Assert.Equal(body, screen[1, 7].StyleId);
        Assert.Equal(body, screen[2, 2].StyleId);
        // gap 2 → 信息列 col 11
        Assert.Equal("jcc v2.1.303", RowText.Of(screen, 0, 11, 12));
        Assert.Equal("glm-4.7 · API 按量计费", RowText.Of(screen, 1, 11, 22));
        Assert.Equal(@"C:\Users\54076", RowText.Of(screen, 2, 11, 14));
        // 信息列样式：bold 名 + 默认空格 + dim 版本 / dim 模型行 / dim cwd
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(bold, screen[0, 11].StyleId);
        Assert.Equal(0, screen[0, 14].StyleId);
        Assert.Equal(dim, screen[0, 15].StyleId);
        Assert.Equal(dim, screen[1, 11].StyleId);
        Assert.Equal(dim, screen[2, 11].StyleId);
    }

    [Fact]
    public void MeasureHeight_IsThreeRowsRegardlessOfWidth()
    {
        Assert.Equal(3, new BannerItem("1.0.0", "glm-4.7", "API 按量计费", "C:\\").MeasureHeight(width: 60));
    }

    [Fact]
    public void Render_NarrowScreenTruncatesInfoColumn()
    {
        using var screen = new Screen(cols: 20, rows: 3);
        var banner = new BannerItem("2.1.303", "glm-4.7", "API 按量计费", "C:\\wd");
        banner.Render(screen, new Rect(0, 0, 20, 3), Theme.Dark);
        // logo 9 列完整保留，信息列截断不折行（对齐基准 truncate 语义；20-11=9 列可用）
        Assert.Equal("▝▜█████▛▘", RowText.Of(screen, 1, 0, 9));
        Assert.Equal("jcc v2.1.", RowText.Of(screen, 0, 11, 9));
    }
}
