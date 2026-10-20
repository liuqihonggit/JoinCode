namespace NativeTui.Tests.Repl.Suggestions;

/// <summary>
/// SuggestionBox 渲染测试 — 对齐基准 PromptInputFooterSuggestions.tsx：
/// 最多 6 条可见；两列布局（/命令名 + 描述，命令列宽固定防布局跳动）；
/// 选中项 Suggestion 色 / 未选中 dim；滚动窗口以选中项为中心；描述超宽截断。
/// </summary>
public sealed class SuggestionBoxTests
{
    /// <summary>建议项构造辅助。</summary>
    private static List<CommandSuggestion> Items(int count, string description = "desc") =>
        Enumerable.Range(0, count).Select(i => new CommandSuggestion($"cmd{i}", description, [], IsHidden: false)).ToList();

    /// <summary>可见高度 = min(条数, 6)。</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(6, 6)]
    [InlineData(10, 6)]
    public void MeasureHeight_MinOfCountAndSix(int count, int expected)
    {
        var box = new SuggestionBox();
        Assert.Equal(expected, box.MeasureHeight(count));
    }

    /// <summary>基础渲染：每行 "/命令名" + 描述（固定列宽），选中首项。</summary>
    [Fact]
    public void Render_ShowsSlashCommandAndDescription()
    {
        using var screen = new Screen(cols: 40, rows: 6);
        var box = new SuggestionBox();
        box.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark, Items(3), selectedIndex: 0, nameColumnWidth: 12);

        Assert.Equal("/cmd0", RowText.Of(screen, 0, 0, 5));
        Assert.Equal("desc", RowText.Of(screen, 0, 12, 4));
        Assert.Equal("/cmd1", RowText.Of(screen, 1, 0, 5));
        Assert.Equal("desc", RowText.Of(screen, 1, 12, 4));
        Assert.Equal("/cmd2", RowText.Of(screen, 2, 0, 5));
        Assert.Equal(Cell.Blank, screen[3, 0]);
    }

    /// <summary>选中项 Suggestion 色，未选中 dim。</summary>
    [Fact]
    public void Render_SelectedUsesSuggestionColor_OthersDim()
    {
        using var screen = new Screen(cols: 40, rows: 6);
        var box = new SuggestionBox();
        box.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark, Items(3), selectedIndex: 1, nameColumnWidth: 12);

        var selected = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Suggestion });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[0, 0].StyleId);
        Assert.Equal(selected, screen[1, 0].StyleId);
        Assert.Equal(dim, screen[2, 0].StyleId);
    }

    /// <summary>命令列固定宽：描述从 nameColumnWidth 起渲染（不随过滤收缩）。</summary>
    [Fact]
    public void Render_DescriptionStartsAtFixedColumn()
    {
        using var screen = new Screen(cols: 40, rows: 6);
        var box = new SuggestionBox();
        box.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark, Items(1), selectedIndex: 0, nameColumnWidth: 12);

        Assert.Equal("desc", RowText.Of(screen, 0, 12, 4));
        Assert.Equal(string.Empty, RowText.Of(screen, 0, 5, 7).TrimEnd());
    }

    /// <summary>滚动窗口：选中项居中（selected=7 → startIndex=4，窗口 [4,10)）。</summary>
    [Fact]
    public void Render_ScrollsSelectedCentered()
    {
        using var screen = new Screen(cols: 40, rows: 6);
        var box = new SuggestionBox();
        box.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark, Items(10), selectedIndex: 7, nameColumnWidth: 12);

        Assert.Equal("/cmd4", RowText.Of(screen, 0, 0, 5).TrimEnd());
        Assert.Equal("/cmd7", RowText.Of(screen, 3, 0, 5).TrimEnd());
        Assert.Equal("/cmd9", RowText.Of(screen, 5, 0, 5).TrimEnd());
    }

    /// <summary>滚动窗口钳制：选中靠前时 startIndex=0。</summary>
    [Fact]
    public void Render_ScrollClampsToStart()
    {
        using var screen = new Screen(cols: 40, rows: 6);
        var box = new SuggestionBox();
        box.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark, Items(10), selectedIndex: 1, nameColumnWidth: 12);

        Assert.Equal("/cmd0", RowText.Of(screen, 0, 0, 5).TrimEnd());
        Assert.Equal("/cmd1", RowText.Of(screen, 1, 0, 5).TrimEnd());
    }

    /// <summary>滚动窗口钳制：选中末项时窗口贴底。</summary>
    [Fact]
    public void Render_ScrollClampsToEnd()
    {
        using var screen = new Screen(cols: 40, rows: 6);
        var box = new SuggestionBox();
        box.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark, Items(10), selectedIndex: 9, nameColumnWidth: 12);

        Assert.Equal("/cmd4", RowText.Of(screen, 0, 0, 5).TrimEnd());
        Assert.Equal("/cmd9", RowText.Of(screen, 5, 0, 5).TrimEnd());
    }

    /// <summary>描述超宽截断：不越过 frame 右界。</summary>
    [Fact]
    public void Render_DescriptionTruncatedToFrame()
    {
        using var screen = new Screen(cols: 20, rows: 6);
        var box = new SuggestionBox();
        var items = new List<CommandSuggestion> { new("cmd0", "0123456789abcdefghij", [], IsHidden: false) };
        box.Render(screen, new Rect(0, 0, 20, 6), Theme.Dark, items, selectedIndex: 0, nameColumnWidth: 12);

        Assert.Equal("01234567", RowText.Of(screen, 0, 12, 8));
    }

    /// <summary>frame 高度不足：按 frame 高度截断渲染行数。</summary>
    [Fact]
    public void Render_TruncatesToFrameHeight()
    {
        using var screen = new Screen(cols: 40, rows: 2);
        var box = new SuggestionBox();
        box.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark, Items(5), selectedIndex: 0, nameColumnWidth: 12);

        Assert.Equal("/cmd0", RowText.Of(screen, 0, 0, 5).TrimEnd());
        Assert.Equal("/cmd1", RowText.Of(screen, 1, 0, 5).TrimEnd());
    }
}
