namespace NativeTui.Tests.Repl;

/// <summary>
/// StructuredDiff 渲染 golden 测试（M7-2）— DiffResultItem 带补丁的工具结果：
/// summary 行沿用 MessageResponse 形态（"  ⎿  " dim 前缀 + 默认正文）；
/// hunk 块 4 空格缩进 — header dim / context dim / added 行 DiffAddedWord 前景 / removed 行 DiffRemovedWord 前景
///（对齐青蛙TUI TranscriptWriter ±行前景色先例，色键换用基准 theme.ts diff 前景键）；
/// patch 行数超预览折叠为 "… +N lines (Ctrl+O)" dim（青蛙TUI Take(3) = summary + 2 行预览先例）。
/// </summary>
public sealed class DiffResultItemTests
{
    /// <summary>构造 hunk（header 固定，行可变）。</summary>
    private static DiffHunk Hunk(params DiffLine[] lines) => new() { Header = "@@ -1,3 +1,4 @@", Lines = lines };

    /// <summary>构造 diff 行。</summary>
    private static DiffLine Line(DiffLineKind kind, string content) => new() { Kind = kind, Content = content };

    [Fact]
    public void Render_SummaryRowKeepsToolResultStyle()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new DiffResultItem("Edited a.cs", [Hunk(Line(DiffLineKind.Added, "new line"))]);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        // row0 margin 留空；row1 = "  ⎿  " dim 前缀 + summary 默认样式（对齐 MessageResponse）
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(new Rune(' '), screen[0, 0].Value);
        Assert.Equal(new Rune('⎿'), screen[1, 2].Value);
        Assert.Equal(dim, screen[1, 2].StyleId);
        Assert.Equal(new Rune('E'), screen[1, 5].Value);
        Assert.Equal(0, screen[1, 5].StyleId);
        Assert.Equal("  ⎿  Edited a.cs", RowText.Of(screen, 1, 0, 16));
    }

    [Fact]
    public void Render_HeaderDimmedAndAddedLineColored()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new DiffResultItem("Edited a.cs", [Hunk(Line(DiffLineKind.Added, "new line"))]);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        // row2 = "    @@ -1,3 +1,4 @@" 整行 dim（4 空格缩进）
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(new Rune(' '), screen[2, 0].Value);
        Assert.Equal(new Rune('@'), screen[2, 4].Value);
        Assert.Equal(dim, screen[2, 4].StyleId);
        Assert.Equal(dim, screen[2, 5].StyleId);
        Assert.Equal("    @@ -1,3 +1,4 @@", RowText.Of(screen, 2, 0, 19));

        // row3 = "    +new line" 整行 DiffAddedWord 前景（前缀字符与正文同 span）
        var added = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.DiffAddedWord });
        Assert.Equal(new Rune('+'), screen[3, 4].Value);
        Assert.Equal(added, screen[3, 4].StyleId);
        Assert.Equal(added, screen[3, 5].StyleId);
        Assert.Equal("    +new line", RowText.Of(screen, 3, 0, 13));
    }

    [Fact]
    public void Render_RemovedLineInDiffRemovedWordColor()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new DiffResultItem("Edited a.cs", [Hunk(Line(DiffLineKind.Removed, "old line"))]);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        var removed = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.DiffRemovedWord });
        Assert.Equal(new Rune('-'), screen[3, 4].Value);
        Assert.Equal(removed, screen[3, 4].StyleId);
        Assert.Equal(removed, screen[3, 7].StyleId);
        Assert.Equal("    -old line", RowText.Of(screen, 3, 0, 13));
    }

    [Fact]
    public void Render_ContextLineDimmed()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new DiffResultItem("Read 10 lines", [Hunk(Line(DiffLineKind.Context, "unchanged"))]);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        // context 行 = "     unchanged"（4 缩进 + ' ' 前缀）整行 dim
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(new Rune(' '), screen[3, 4].Value);
        Assert.Equal(dim, screen[3, 4].StyleId);
        Assert.Equal(new Rune('u'), screen[3, 5].Value);
        Assert.Equal(dim, screen[3, 5].StyleId);
        Assert.Equal("     unchanged", RowText.Of(screen, 3, 0, 14));
    }

    [Fact]
    public void Render_CollapsesBeyondPreviewWithHint()
    {
        // 1 hunk = header + 4 行 → patch 5 行 > 预览 2 行 → 渲染 header + 首行 + 折叠提示
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new DiffResultItem("Edited a.cs", [Hunk(
            Line(DiffLineKind.Context, "keep"),
            Line(DiffLineKind.Removed, "drop"),
            Line(DiffLineKind.Added, "add1"),
            Line(DiffLineKind.Added, "add2"))]);

        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        // row2 header（预览第 1 行）→ row3 首个 diff 行（预览第 2 行）→ row4 折叠提示 dim
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("    @@ -1,3 +1,4 @@", RowText.Of(screen, 2, 0, 19));
        Assert.Equal("     keep", RowText.Of(screen, 3, 0, 9));
        Assert.Equal("    … +3 lines (Ctrl+O)", RowText.Of(screen, 4, 0, 23));
        Assert.Equal(dim, screen[4, 4].StyleId);
    }

    [Fact]
    public void Render_MultipleHunksPreviewSpansHunks()
    {
        // 2 hunks（各 header + 1 行）→ patch 4 行 > 预览 2 → 预览行 = h1 header + h1 line，折叠 +2
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new DiffResultItem("2 files", [
            Hunk(Line(DiffLineKind.Added, "a")),
            new DiffHunk { Header = "@@ -9 +9 @@", Lines = [Line(DiffLineKind.Removed, "b")] },
        ]);

        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal("    @@ -1,3 +1,4 @@", RowText.Of(screen, 2, 0, 19));
        Assert.Equal("    +a", RowText.Of(screen, 3, 0, 6));
        Assert.Equal("    … +2 lines (Ctrl+O)", RowText.Of(screen, 4, 0, 23));
    }

    [Fact]
    public void MeasureHeight_MarginPlusSummaryPlusRenderedPatchRows()
    {
        // patch 2 行（header+1 行）恰在预览内 → 1 margin + 1 summary + 2 = 4
        var fits = new DiffResultItem("s", [Hunk(Line(DiffLineKind.Added, "x"))]);
        Assert.Equal(4, fits.MeasureHeight(width: 80));

        // patch 5 行超预览 → 1 + 1 + 2 预览 + 1 折叠提示 = 5
        var collapsed = new DiffResultItem("s", [Hunk(
            Line(DiffLineKind.Context, "a"),
            Line(DiffLineKind.Context, "b"),
            Line(DiffLineKind.Context, "c"),
            Line(DiffLineKind.Context, "d"))]);
        Assert.Equal(5, collapsed.MeasureHeight(width: 80));

        // 2 hunks 共 4 行 → 折叠 → 5
        var multiHunk = new DiffResultItem("s", [
            Hunk(Line(DiffLineKind.Added, "a")),
            Hunk(Line(DiffLineKind.Removed, "b")),
        ]);
        Assert.Equal(5, multiHunk.MeasureHeight(width: 80));
    }

    [Fact]
    public void Patch_ExposedForDriverAssertions()
    {
        var hunks = new[] { Hunk(Line(DiffLineKind.Added, "x")) };
        var item = new DiffResultItem("summary", hunks);

        Assert.Equal("summary", item.Summary);
        Assert.Same(hunks, item.Patch);
    }
}
