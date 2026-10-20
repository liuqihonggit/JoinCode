namespace NativeTui.Tests.Components;

/// <summary>
/// ScrollBox golden 测试 — 像素级对齐基准 ink/components/ScrollBox.tsx 视口语义：
/// sticky 钉底（首渲染即生效）、scrollBy 打破 sticky、位置跟随（非 sticky 但在旧 max 处内容增长 → 重新钉底）、
/// 视口裁剪 + -scrollTop 平移、部分可见项按可见行区间裁剪。
/// </summary>
public sealed class ScrollBoxTests
{
    private sealed class LineItem(char c) : IScrollItem
    {
        public int MeasureHeight(int width) => 1;

        public void Render(Screen screen, Rect frame, Theme theme) =>
            screen.WriteText(frame.Y, frame.X, c.ToString(), 0, frame.Right);
    }

    private sealed class TallItem(char c, int height) : IScrollItem
    {
        public int MeasureHeight(int width) => height;

        public void Render(Screen screen, Rect frame, Theme theme)
        {
            for (var row = 0; row < frame.Height; row++)
            {
                screen.WriteText(frame.Y + row, frame.X, c.ToString(), 0, frame.Right);
            }
        }
    }

    [Fact]
    public void Insert_BeforeTailKeepsTailItemLast()
    {
        // spinner 常驻尾部：新消息必须插入到它前面（REPL 滚动区尾部 spinner 语义）
        using var screen = new Screen(cols: 10, rows: 5);
        var box = new ScrollBox(stickyScroll: true);
        box.Add(new LineItem('a'));
        box.Add(new LineItem('z')); // z 模拟常驻 spinner
        box.Insert(box.Count - 1, new LineItem('m'));
        Assert.Equal(3, box.Count);
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        // 内容 a m z，视口 3 行 → 全部可见且 z 仍在最后
        Assert.Equal(new Rune('a'), screen[0, 0].Value);
        Assert.Equal(new Rune('m'), screen[1, 0].Value);
        Assert.Equal(new Rune('z'), screen[2, 0].Value);
    }

    [Fact]
    public void Insert_AtZero_Prepends()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        var box = new ScrollBox(stickyScroll: true);
        box.Add(new LineItem('b'));
        box.Insert(0, new LineItem('a'));
        Assert.Equal(2, box.Count);
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        Assert.Equal(new Rune('a'), screen[0, 0].Value);
        Assert.Equal(new Rune('b'), screen[1, 0].Value);
    }

    [Fact]
    public void Sticky_PinsToBottomOnFirstRender()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        var box = new ScrollBox(stickyScroll: true);
        foreach (var c in "abcde")
        {
            box.Add(new LineItem(c));
        }

        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        // 视口 3 行，内容 5 行，sticky 钉底 → 显示 c d e
        Assert.Equal(new Rune('c'), screen[0, 0].Value);
        Assert.Equal(new Rune('d'), screen[1, 0].Value);
        Assert.Equal(new Rune('e'), screen[2, 0].Value);
        Assert.True(box.Sticky);
        Assert.Equal(2, box.ScrollTop);
    }

    [Fact]
    public void ContentShorterThanViewport_RendersFromTop()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        var box = new ScrollBox(stickyScroll: true);
        box.Add(new LineItem('a'));
        box.Add(new LineItem('b'));
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        Assert.Equal(new Rune('a'), screen[0, 0].Value);
        Assert.Equal(new Rune('b'), screen[1, 0].Value);
        Assert.Equal(Cell.Blank, screen[2, 0]);
        Assert.Equal(0, box.ScrollTop);
    }

    [Fact]
    public void ScrollBy_BreaksStickyAndShowsTop()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        var box = new ScrollBox(stickyScroll: true);
        foreach (var c in "abcde")
        {
            box.Add(new LineItem(c));
        }

        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        box.ScrollBy(-2);
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        Assert.Equal(new Rune('a'), screen[0, 0].Value);
        Assert.Equal(new Rune('b'), screen[1, 0].Value);
        Assert.Equal(new Rune('c'), screen[2, 0].Value);
        Assert.False(box.Sticky);
    }

    [Fact]
    public void Add_WhileScrolledUp_DoesNotFollow()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        var box = new ScrollBox(stickyScroll: true);
        foreach (var c in "abcde")
        {
            box.Add(new LineItem(c));
        }

        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        box.ScrollBy(-2);
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        box.Add(new LineItem('f'));
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        // 上滚状态（scrollTop=0 ≠ 旧 max=2）内容增长 → 不跟随，仍显示 a b c
        Assert.Equal(new Rune('a'), screen[0, 0].Value);
        Assert.Equal(new Rune('b'), screen[1, 0].Value);
        Assert.Equal(new Rune('c'), screen[2, 0].Value);
        Assert.False(box.Sticky);
    }

    [Fact]
    public void AtBottomNonSticky_GrowsContent_Follows()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        var box = new ScrollBox(stickyScroll: true);
        foreach (var c in "abcde")
        {
            box.Add(new LineItem(c));
        }

        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        // 打破 sticky 后滚回底部（scrollTop == 旧 max）
        box.ScrollBy(-1);
        box.ScrollBy(1);
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        Assert.False(box.Sticky);
        // 在底部时内容增长 → 位置跟随重新钉底（基准 positional follow）
        box.Add(new LineItem('f'));
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        Assert.Equal(new Rune('d'), screen[0, 0].Value);
        Assert.Equal(new Rune('e'), screen[1, 0].Value);
        Assert.Equal(new Rune('f'), screen[2, 0].Value);
        Assert.True(box.Sticky);
    }

    [Fact]
    public void ScrollToBottom_RePinsAfterManualScroll()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        var box = new ScrollBox(stickyScroll: true);
        foreach (var c in "abcdef")
        {
            box.Add(new LineItem(c));
        }

        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        box.ScrollBy(-3);
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        box.ScrollToBottom();
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        Assert.Equal(new Rune('d'), screen[0, 0].Value);
        Assert.Equal(new Rune('e'), screen[1, 0].Value);
        Assert.Equal(new Rune('f'), screen[2, 0].Value);
        Assert.True(box.Sticky);
    }

    [Fact]
    public void ScrollBy_ClampsAtEdges()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        var box = new ScrollBox(stickyScroll: true);
        foreach (var c in "abcde")
        {
            box.Add(new LineItem(c));
        }

        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        box.ScrollBy(-100);
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        Assert.Equal(0, box.ScrollTop);
        Assert.Equal(new Rune('a'), screen[0, 0].Value);

        box.ScrollBy(100);
        box.Render(screen, new Rect(0, 0, 10, 3), Theme.Dark);
        Assert.Equal(2, box.ScrollTop);
        Assert.Equal(new Rune('c'), screen[0, 0].Value);
        Assert.Equal(new Rune('e'), screen[2, 0].Value);
    }

    [Fact]
    public void TallItem_PartialVisibility_ClippedAndTranslated()
    {
        using var screen = new Screen(cols: 10, rows: 2);
        var box = new ScrollBox(stickyScroll: true);
        box.Add(new TallItem('x', 3));
        box.Add(new LineItem('a'));
        box.Render(screen, new Rect(0, 0, 10, 2), Theme.Dark);
        // 内容 4 行视口 2 行钉底 scrollTop=2：高项仅第 3 行可见（平移到屏幕行 0），随后是 a
        Assert.Equal(new Rune('x'), screen[0, 0].Value);
        Assert.Equal(new Rune('a'), screen[1, 0].Value);
    }
}
