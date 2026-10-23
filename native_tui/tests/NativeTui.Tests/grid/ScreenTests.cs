namespace NativeTui.Tests.ScreenTests;

/// <summary>
/// Rune 宽度测试 — East Asian Width 简化表（对齐基准 get-east-asian-width 依赖的核心区间）。
/// Ambiguous 字符（⏺ ✻ ● 等）按非 CJK 上下文宽度 1 处理 — 基准 figures.ts 的 darwin/win 差异正是 Ambiguous。
/// </summary>
public sealed class RuneWidthTests
{
    [Theory]
    [InlineData('a', 1)]
    [InlineData('Z', 1)]
    [InlineData('❯', 1)]
    [InlineData('⏺', 1)]
    [InlineData('●', 1)]
    [InlineData('✻', 1)]
    [InlineData('⎿', 1)]
    [InlineData('∙', 1)]
    public void AmbiguousAndAscii_Width1(char ch, int expected)
    {
        Assert.Equal(expected, RuneWidth.Of(new Rune(ch)));
    }

    [Theory]
    [InlineData('中', 2)]
    [InlineData('文', 2)]
    [InlineData('！', 2)]   // U+FF01 全角
    [InlineData('）', 2)]   // U+FF09
    public void CjkAndFullwidth_Width2(char ch, int expected)
    {
        Assert.Equal(expected, RuneWidth.Of(new Rune(ch)));
    }

    [Fact]
    public void EmojiSupplementaryPlane_Width2()
    {
        // U+1F600 😀
        Assert.Equal(2, RuneWidth.Of(new Rune(0x1F600)));
    }

    [Fact]
    public void ZeroWidthJoiner_Width0()
    {
        Assert.Equal(0, RuneWidth.Of(new Rune(0x200D)));
    }

    /// <summary>EAW Wide BMP 区段补全（基准经 eastAsianWidth 库覆盖全表 —
    /// 此前缺 0x231A⌚/0x23E9⏩/0x26AA⚪/0x2B50⭐ 等区段，错列 1 vs 实际 2）。</summary>
    [Theory]
    [InlineData(0x231A)]  // ⌚ 手表
    [InlineData(0x231B)]  // ⌛ 沙漏
    [InlineData(0x23E9)]  // ⏩ 快进
    [InlineData(0x23EC)]  // ⏪ 快退
    [InlineData(0x23F0)]  // ⏰ 闹钟
    [InlineData(0x23F3)]  // ⏳ 沙漏流
    [InlineData(0x25FD)]  // ◽ 中白小方
    [InlineData(0x2614)]  // ☔ 雨伞
    [InlineData(0x2648)]  // ♈ 白羊座
    [InlineData(0x2653)]  // ♓ 双鱼座
    [InlineData(0x267F)]  // ♿ 轮椅
    [InlineData(0x2693)]  // ⚓ 锚
    [InlineData(0x26A1)]  // ⚡ 高压电
    [InlineData(0x26AA)]  // ⚪ 白圆
    [InlineData(0x26AB)]  // ⚫ 黑圆
    [InlineData(0x26BD)]  // ⚽ 足球
    [InlineData(0x26C4)]  // ⛄ 雪人
    [InlineData(0x26CE)]  // ⛎ 蛇夫座
    [InlineData(0x26D4)]  // ⛔ 禁止
    [InlineData(0x26EA)]  // ⛪ 教堂
    [InlineData(0x26F2)]  // ⛲ 喷泉
    [InlineData(0x26F5)]  // ⛵ 帆船
    [InlineData(0x26FA)]  // ⛺ 帐篷
    [InlineData(0x26FD)]  // ⛽ 加油站
    [InlineData(0x2705)]  // ✅ 白勾
    [InlineData(0x270A)]  // ✊ 举拳
    [InlineData(0x2728)]  // ✨ 闪光
    [InlineData(0x274C)]  // ❌ 叉
    [InlineData(0x2757)]  // ❗ 感叹
    [InlineData(0x2795)]  // ➕ 加
    [InlineData(0x27B0)]  // ➰ 弯曲
    [InlineData(0x27BF)]  // ➿ 双弯曲
    [InlineData(0x2B1B)]  // ⬛ 黑大方
    [InlineData(0x2B50)]  // ⭐ 星
    [InlineData(0x2B55)]  // ⭕ 红圆
    public void EastAsianWideBmpSegments_Width2(int codepoint)
    {
        Assert.Equal(2, RuneWidth.Of(new Rune(codepoint)));
    }

    /// <summary>grapheme 整块宽度（DSG044 L1 — 基准 stringWidth.ts 语义）：ZWJ 链/RI 对恒 2 列、
    /// 组合符附加不增宽（max 非 sum）、普通 cluster = 主字符宽度。</summary>
    [Fact]
    public void WidthOfGrapheme_EmojiFormsAndPlainClusters()
    {
        // ZWJ 家庭组合（3 emoji + 2 ZWJ）— rune 逐个计宽会得 6 列，grapheme 整块 2 列
        Assert.Equal(2, RuneWidth.WidthOfGrapheme("\U0001F468\u200D\U0001F469\u200D\U0001F467"));
        // RI 旗帜对 — 2 列
        Assert.Equal(2, RuneWidth.WidthOfGrapheme("\U0001F1E8\U0001F1F3"));
        // 组合符附加 — 主字符宽 1
        Assert.Equal(1, RuneWidth.WidthOfGrapheme("e\u0301"));
        // 变体选择器附加 — 主字符宽（Ambiguous ✌ = 1）
        Assert.Equal(1, RuneWidth.WidthOfGrapheme("\u270C\uFE0F"));
        // 普通 cluster
        Assert.Equal(2, RuneWidth.WidthOfGrapheme("中"));
        Assert.Equal(1, RuneWidth.WidthOfGrapheme("a"));
    }
}

/// <summary>
/// Screen cell 网格测试 — 写入/宽字符占位/越界截断/Resize/golden 序列化。
/// </summary>
public sealed class ScreenTests
{
    [Fact]
    public void NewScreen_AllBlankDefaultStyle()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        Assert.Equal(10, screen.Cols);
        Assert.Equal(3, screen.Rows);
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 10; col++)
            {
                Assert.Equal(' ', screen[row, col].Value.Value);
                Assert.Equal(0, screen[row, col].StyleId);
                Assert.Equal(CellWidth.Default, screen[row, col].Width);
            }
        }
    }

    [Fact]
    public void WriteText_Ascii_PlacedSequentially()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var end = screen.WriteText(0, 0, "hello");
        Assert.Equal(5, end);
        Assert.Equal('h', screen[0, 0].Value.Value);
        Assert.Equal('o', screen[0, 4].Value.Value);
        Assert.Equal(' ', screen[0, 5].Value.Value);
    }

    [Fact]
    public void WriteText_StyleIdApplied()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(0, 0, "ab", bold);
        Assert.Equal(bold, screen[0, 0].StyleId);
        Assert.Equal(bold, screen[0, 1].StyleId);
    }

    [Fact]
    public void WriteText_Cjk_OccupiesWidePlusSpacerTail()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var end = screen.WriteText(0, 0, "中文");
        Assert.Equal(4, end);
        Assert.Equal('中', screen[0, 0].Value.Value);
        Assert.Equal(CellWidth.Wide, screen[0, 0].Width);
        Assert.Equal(CellWidth.SpacerTail, screen[0, 1].Width);
        Assert.Equal('文', screen[0, 2].Value.Value);
        Assert.Equal(CellWidth.SpacerTail, screen[0, 3].Width);
    }

    [Fact]
    public void WriteText_MixedAsciiAndCjk()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var end = screen.WriteText(0, 0, "a中b");
        Assert.Equal(4, end);
        Assert.Equal('a', screen[0, 0].Value.Value);
        Assert.Equal(CellWidth.Wide, screen[0, 1].Width);
        Assert.Equal(CellWidth.SpacerTail, screen[0, 2].Width);
        Assert.Equal('b', screen[0, 3].Value.Value);
    }

    [Fact]
    public void WriteText_WideCharAtLastColumn_Truncated()
    {
        using var screen = new Screen(cols: 3, rows: 1);
        var end = screen.WriteText(0, 2, "中");
        // 最后一列放不下宽字符 → 停止
        Assert.Equal(2, end);
        Assert.Equal(' ', screen[0, 2].Value.Value);
    }

    [Fact]
    public void WriteText_PastRowEnd_StopsAtBoundary()
    {
        using var screen = new Screen(cols: 3, rows: 1);
        var end = screen.WriteText(0, 1, "abcd");
        Assert.Equal(3, end);
        Assert.Equal('a', screen[0, 1].Value.Value);
        Assert.Equal('b', screen[0, 2].Value.Value);
    }

    [Fact]
    public void WriteText_EmojiSupplementaryPlane_Wide()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var end = screen.WriteText(0, 0, "😀");
        Assert.Equal(2, end);
        Assert.Equal(CellWidth.Wide, screen[0, 0].Width);
        Assert.Equal(0x1F600, screen[0, 0].Value.Value);
    }

    [Fact]
    public void Clear_ResetsAllCells()
    {
        using var screen = new Screen(cols: 5, rows: 2);
        screen.WriteText(0, 0, "hello");
        screen.Clear();
        Assert.Equal(' ', screen[0, 0].Value.Value);
    }

    [Fact]
    public void Resize_PreservesTopLeft()
    {
        using var screen = new Screen(cols: 10, rows: 5);
        screen.WriteText(0, 0, "keep");
        screen.WriteText(4, 0, "cut");
        screen.Resize(cols: 6, rows: 2);
        Assert.Equal(6, screen.Cols);
        Assert.Equal(2, screen.Rows);
        Assert.Equal('k', screen[0, 0].Value.Value);
        Assert.Equal('p', screen[0, 3].Value.Value);
        Assert.Equal(' ', screen[1, 0].Value.Value);
    }

    [Fact]
    public void ToAnsiText_PlainAscii_NoEscape()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        screen.WriteText(0, 0, "hi");
        Assert.Equal("hi", screen.ToAnsiText());
    }

    [Fact]
    public void ToAnsiText_StyledSegment_EmitsTransitionAndResetsAtEol()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var orange = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(215, 119, 87) });
        screen.WriteText(0, 0, "ab", orange);
        // 行尾停在非默认样式 → 必须重置（否则终端状态泄漏到下一行）
        Assert.Equal("\x1b[38;2;215;119;87mab\x1b[39m", screen.ToAnsiText());
    }

    [Fact]
    public void ToAnsiText_StyleChangeMidLine_EmitsBothTransitions()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(0, 0, "a", bold);
        screen.WriteText(0, 1, "b");
        Assert.Equal("\x1b[1ma\x1b[22mb", screen.ToAnsiText());
    }

    [Fact]
    public void ToAnsiText_CjkSpacerTailNotDuplicated()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        screen.WriteText(0, 0, "中");
        // 宽字符只输出 rune 一次，SpacerTail 不重复
        Assert.Equal("中", screen.ToAnsiText());
    }

    [Fact]
    public void ToAnsiText_TrailingStyleResetToDefaultNotEmitted()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(0, 0, "a", bold);
        screen.WriteText(0, 1, "b");
        // 行尾回到 Default 不需要发序列
        Assert.Equal("\x1b[1ma\x1b[22mb", screen.ToAnsiText());
    }

    [Fact]
    public void SharedStylePool_SameStyleSameIdAcrossScreens()
    {
        // 帧双缓冲：前后帧 Screen 必须共享样式池，否则同一样式拿到不同 ID → 假 diff
        using var front = new Screen(cols: 10, rows: 1, sharedStyles: new StylePool());
        using var back = new Screen(cols: 10, rows: 1, sharedStyles: front.Styles);
        var boldFront = front.Styles.GetOrAdd(new TextStyle { Bold = true });
        var boldBack = back.Styles.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal(boldFront, boldBack);
        Assert.Same(front.Styles, back.Styles);
    }

    [Fact]
    public void SharedStylePool_PingPongIdenticalFrames_DiffEmpty()
    {
        // 双缓冲渲染相同内容 → CellDiffer 零输出（FrameComposer 无变化零输出的前提）
        var pool = new StylePool();
        using var front = new Screen(cols: 20, rows: 3, sharedStyles: pool);
        using var back = new Screen(cols: 20, rows: 3, sharedStyles: pool);
        RenderDemoFrame(front);
        RenderDemoFrame(back);
        Assert.Equal(string.Empty, new CellDiffer().RenderFrame(front, back));
    }

    private static void RenderDemoFrame(Screen screen)
    {
        var theme = Theme.Dark;
        var claude = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Claude });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(0, 0, "✢ Working…", claude);
        screen.WriteText(1, 0, "(0.5s · ↓ 12 tokens)", dim);
        screen.WriteText(2, 0, "❯ hello", 0);
    }
}
