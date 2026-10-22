namespace NativeTui.Tests.Repl;

/// <summary>
/// QuickOpenDialog 渲染测试（QuickOpen 对话框域 — 对齐 ModelPickerDialog 形态学骨架）：
/// 固定行序（margin/permission 边框/标题 "快速打开"/查询行/空行/列表/空行/footer Byline）+
/// 焦点行 ❯ suggestion 高亮 + 空态提示（空查询/无匹配两分支）+ 溢出行 " … 及 N 项" +
/// 长路径显示宽截断尾缀 … + 导航后高亮跟随 + 关闭后零高度零渲染 + MeasureHeight 行数一致性。
/// </summary>
public sealed class DialogQuickOpenRenderTests
{
    /// <summary>固定行序 — 边框/标题/查询占位/结果列表/footer 全链 golden（3 文件 40 宽）。</summary>
    [Fact]
    public void Render_RowLayout_BorderTitleQueryListFooter()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var dialog = new QuickOpenDialog(["README.md", "src/Program.cs", "docs/adr/0137.md"]);

        Assert.Equal(10, dialog.MeasureHeight(40));
        dialog.Render(screen, new Rect(0, 0, 40, 10), Theme.Dark);

        // 行序：margin0 / 边框1 / 标题2 / 查询3 / 空4 / 结果5-7 / 空8 / footer9
        Assert.Equal(new string('─', 40), RowText.Of(screen, 1, 0, 40));
        Assert.Equal(" 快速打开", RowText.Of(screen, 2, 0, 40).TrimEnd());
        Assert.Equal(" ❯ 输入以搜索文件…", RowText.Of(screen, 3, 0, 40).TrimEnd());
        Assert.Equal("❯ README.md", RowText.Of(screen, 5, 0, 40).TrimEnd());
        Assert.Equal("  src/Program.cs", RowText.Of(screen, 6, 0, 40).TrimEnd());
        Assert.Equal("  docs/adr/0137.md", RowText.Of(screen, 7, 0, 40).TrimEnd());
        Assert.Equal(" ↑↓ 导航 · enter 打开 · esc 关闭", RowText.Of(screen, 9, 0, 40).TrimEnd());
    }

    /// <summary>焦点行高亮 — ❯ 与路径 suggestion 色；非焦点行默认样式（对齐 RenderModelListRow 分色）。</summary>
    [Fact]
    public void Render_FocusedRow_SuggestionColored()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var dialog = new QuickOpenDialog(["README.md", "src/Program.cs"]);

        dialog.Render(screen, new Rect(0, 0, 40, 10), Theme.Dark);

        var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Suggestion });
        Assert.Equal(suggestion, screen[5, 0].StyleId);
        Assert.Equal(suggestion, screen[5, 2].StyleId);
        Assert.Equal(0, screen[6, 0].StyleId);
        Assert.Equal(0, screen[6, 2].StyleId);
    }

    /// <summary>导航后渲染 — 高亮跟随焦点行（❯ 下移到第二行）。</summary>
    [Fact]
    public void Render_AfterNavigation_FocusMovesToSecondRow()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var dialog = new QuickOpenDialog(["README.md", "src/Program.cs", "docs/adr/0137.md"]);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        dialog.Render(screen, new Rect(0, 0, 40, 10), Theme.Dark);

        Assert.Equal("  README.md", RowText.Of(screen, 5, 0, 40).TrimEnd());
        Assert.Equal("❯ src/Program.cs", RowText.Of(screen, 6, 0, 40).TrimEnd());
    }

    /// <summary>空查询行 — 显示已输入查询文本（截断后的原文）。</summary>
    [Fact]
    public void Render_QueryRow_ShowsTypedQuery()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var dialog = new QuickOpenDialog(["src/App.cs"]);

        dialog.TryHandleKey(KeyPress.Char("a"));
        dialog.Render(screen, new Rect(0, 0, 40, 10), Theme.Dark);

        Assert.Equal(" ❯ a", RowText.Of(screen, 3, 0, 40).TrimEnd());
        Assert.Equal("❯ src/App.cs", RowText.Of(screen, 5, 0, 40).TrimEnd());
    }

    /// <summary>空清单 — 列表区 1 行 dim 提示 "输入以搜索…"（基准空查询分支）。</summary>
    [Fact]
    public void Render_EmptyFileList_ShowsEmptyQueryHint()
    {
        using var screen = new Screen(cols: 40, rows: 8);
        var dialog = new QuickOpenDialog([]);

        Assert.Equal(8, dialog.MeasureHeight(40));
        dialog.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);

        Assert.Equal(" 输入以搜索…", RowText.Of(screen, 5, 0, 40).TrimEnd());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[5, 1].StyleId);
    }

    /// <summary>无匹配 — 列表区 1 行 dim 提示 "无匹配文件"（基准非空查询分支）。</summary>
    [Fact]
    public void Render_NoResults_ShowsNoMatchHint()
    {
        using var screen = new Screen(cols: 40, rows: 8);
        var dialog = new QuickOpenDialog(["README.md"]);

        dialog.TryHandleKey(KeyPress.Char("z"));
        dialog.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);

        Assert.Equal(" 无匹配文件", RowText.Of(screen, 5, 0, 40).TrimEnd());
    }

    /// <summary>结果超出可见 — 溢出行 " … 及 N 项" dim + 可见 8 行 + MeasureHeight 含溢出行。</summary>
    [Fact]
    public void Render_MoreResultsThanVisible_ShowsOverflowRow()
    {
        using var screen = new Screen(cols: 40, rows: 16);
        var files = Enumerable.Range(1, 12).Select(i => $"f{i:D2}.txt").ToArray();
        var dialog = new QuickOpenDialog(files);

        Assert.Equal(16, dialog.MeasureHeight(40));
        dialog.Render(screen, new Rect(0, 0, 40, 16), Theme.Dark);

        // 行序：margin0 / 边框1 / 标题2 / 查询3 / 空4 / 结果5-12（可见 8）/ 溢出13 / 空14 / footer15
        Assert.Equal("❯ f01.txt", RowText.Of(screen, 5, 0, 40).TrimEnd());
        Assert.Equal("  f08.txt", RowText.Of(screen, 12, 0, 40).TrimEnd());
        Assert.Equal(" … 及 4 项", RowText.Of(screen, 13, 0, 40).TrimEnd());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[13, 1].StyleId);
        Assert.Equal(" ↑↓ 导航 · enter 打开 · esc 关闭", RowText.Of(screen, 15, 0, 40).TrimEnd());
    }

    /// <summary>长路径 — 显示宽截断尾缀 …（可用宽 = 帧宽 - 5，RuneWidth 计量）。</summary>
    [Fact]
    public void Render_LongPath_TruncatedWithEllipsis()
    {
        using var screen = new Screen(cols: 30, rows: 10);
        var longPath = "src/very/deeply/nested/directory/structure/LongFileName.cs";
        var dialog = new QuickOpenDialog([longPath]);

        dialog.Render(screen, new Rect(0, 0, 30, 10), Theme.Dark);

        // 可用宽 = 30 - 5 = 25 → 截到 24 列 + "…"（ASCII 路径 24 字符）
        var expected = string.Concat(longPath.AsSpan(0, 24), "…");
        Assert.Equal(expected, RowText.Of(screen, 5, 2, 25));
    }

    /// <summary>中文路径 — 宽字符正确写入（CJK 2 列，不越界不截断可用范围内）。</summary>
    [Fact]
    public void Render_ChinesePath_WideCharsRendered()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var dialog = new QuickOpenDialog(["src/服务/配置.json"]);

        dialog.Render(screen, new Rect(0, 0, 40, 10), Theme.Dark);

        Assert.Equal("❯ src/服务/配置.json", RowText.Of(screen, 5, 0, 40).TrimEnd());
    }

    /// <summary>关闭后 — 零高度零渲染（宿主消费 IsClosed 移除项前的瞬时防御态）。</summary>
    [Fact]
    public void Render_AfterClose_RendersNothing()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var dialog = new QuickOpenDialog(["README.md"]);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape));

        Assert.Equal(0, dialog.MeasureHeight(40));
        dialog.Render(screen, new Rect(0, 0, 40, 10), Theme.Dark);

        Assert.Equal(string.Empty, RowText.Of(screen, 2, 0, 40).TrimEnd());
        Assert.Equal(string.Empty, RowText.Of(screen, 5, 0, 40).TrimEnd());
    }

    /// <summary>MeasureHeight/Render 同参数 — 行数漂移防御锚（空态 1 行提示 vs 可见 N 行）。</summary>
    [Theory]
    [InlineData(1, 8)]
    [InlineData(5, 12)]
    [InlineData(12, 16)]
    public void MeasureHeight_MatchesListRows(int fileCount, int expectedHeight)
    {
        var files = Enumerable.Range(1, fileCount).Select(i => $"f{i:D2}.txt").ToArray();
        var dialog = new QuickOpenDialog(files);

        Assert.Equal(expectedHeight, dialog.MeasureHeight(40));
    }
}
