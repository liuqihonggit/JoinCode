namespace NativeTui.Tests.Repl;

/// <summary>
/// StructuredDiff 渲染 golden 测试（M7-2）— DiffResultItem 带补丁的工具结果：
/// summary 行沿用 MessageResponse 形态（"  ⎿  " dim 前缀 + 默认正文）；
/// hunk 块 4 空格缩进 — header dim / context dim / added 行 DiffAddedWord 前景 / removed 行 DiffRemovedWord 前景
///（对齐青蛙TUI TranscriptWriter ±行前景色先例，色键换用基准 theme.ts diff 前景键）；
/// patch 全量渲染（基准 update 渲染完整 diff — FileWriteTool/UI.tsx L138-141），
/// 仅超 10 行预览上限折叠为 "… +N lines (Ctrl+O)"（上限对齐基准 Write MAX_LINES_TO_RENDER=10 先例）。
/// </summary>
public sealed class DiffResultItemTests
{
    /// <summary>构造 hunk（header 固定，行可变）。</summary>
    private static DiffHunk Hunk(params DiffLine[] lines) => new() { Header = "@@ -1,3 +1,4 @@", Lines = lines };

    /// <summary>构造 diff 行。</summary>
    private static DiffLine Line(DiffLineKind kind, string content) => new() { Kind = kind, Content = content };

    /// <summary>构造超预览上限的 patch（header + N 行，总行数 = 1 + N）。</summary>
    private static DiffHunk LongHunk(int lineCount) => new()
    {
        Header = "@@ -1,3 +1,4 @@",
        Lines = Enumerable.Range(0, lineCount).Select(i => Line(DiffLineKind.Context, $"line {i}")).ToArray(),
    };

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
    public void Render_DemoPatchShowsRedAndGreenLines()
    {
        // 回归（用户真实终端验收反馈）：SimulatedEngine 演示补丁渲染后预览窗口内必须可见红绿行 —
        // 4 行 hunk 全量渲染（≤10 行预览上限），Removed 行 DiffRemovedWord + Added 行 DiffAddedWord 都在屏上
        var patch = SimulatedEngine.SimulatedPatch;
        using var screen = new Screen(cols: 80, rows: 2 + 1 + 1 + patch.Sum(static h => 1 + h.Lines.Length));

        var item = new DiffResultItem("Updated demo/simulated.cs (simulated)", patch);
        item.Render(screen, new Rect(0, 0, screen.Cols, screen.Rows), Theme.Dark);

        var removed = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.DiffRemovedWord });
        var added = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.DiffAddedWord });
        Assert.Equal("-old behavior", RowText.Of(screen, 3, 4, 13));
        Assert.Equal(removed, screen[3, 4].StyleId);
        Assert.Equal("+new behavior + diff render", RowText.Of(screen, 4, 4, 27));
        Assert.Equal(added, screen[4, 4].StyleId);
    }

    [Fact]
    public void Render_CollapsesBeyondPreviewWithHint()
    {
        // header + 12 行 = 13 行 > 预览上限 10 → 渲染 header + 前 9 行 + 折叠提示 +3
        using var screen = new Screen(cols: 40, rows: 2 + 10 + 1);
        var item = new DiffResultItem("Edited a.cs", [LongHunk(12)]);

        item.Render(screen, new Rect(0, 0, 40, screen.Rows), Theme.Dark);

        // row2 header → row3..row11 预览 9 行 → row12 折叠提示 dim
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("    @@ -1,3 +1,4 @@", RowText.Of(screen, 2, 0, 19));
        Assert.Equal("     line 0", RowText.Of(screen, 3, 0, 11));
        Assert.Equal("     line 8", RowText.Of(screen, 11, 0, 11));
        Assert.Equal("    … +3 lines (Ctrl+O)", RowText.Of(screen, 12, 0, 23));
        Assert.Equal(dim, screen[12, 4].StyleId);
    }

    [Fact]
    public void Render_MultipleHunksAllRendered()
    {
        // 2 hunks（各 header + 1 行）共 4 行 ≤ 预览上限 → 全量渲染无折叠
        using var screen = new Screen(cols: 40, rows: 6);
        var item = new DiffResultItem("2 files", [
            Hunk(Line(DiffLineKind.Added, "a")),
            new DiffHunk { Header = "@@ -9 +9 @@", Lines = [Line(DiffLineKind.Removed, "b")] },
        ]);

        item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);

        Assert.Equal("    @@ -1,3 +1,4 @@", RowText.Of(screen, 2, 0, 19));
        Assert.Equal("    +a", RowText.Of(screen, 3, 0, 6));
        Assert.Equal("    @@ -9 +9 @@", RowText.Of(screen, 4, 0, 15));
        Assert.Equal("    -b", RowText.Of(screen, 5, 0, 6));
    }

    [Fact]
    public void MeasureHeight_MarginPlusSummaryPlusRenderedPatchRows()
    {
        // patch 2 行（header+1 行）→ 1 margin + 1 summary + 2 = 4（预览内全渲染）
        var fits = new DiffResultItem("s", [Hunk(Line(DiffLineKind.Added, "x"))]);
        Assert.Equal(4, fits.MeasureHeight(width: 80));

        // patch 5 行 ≤ 预览上限 10 → 1 + 1 + 5 = 7（全渲染无折叠）
        var collapsed = new DiffResultItem("s", [Hunk(
            Line(DiffLineKind.Context, "a"),
            Line(DiffLineKind.Context, "b"),
            Line(DiffLineKind.Context, "c"),
            Line(DiffLineKind.Context, "d"))]);
        Assert.Equal(7, collapsed.MeasureHeight(width: 80));

        // patch 13 行（header+12）> 预览 10 → 1 + 1 + 10 预览 + 1 折叠提示 = 13
        var overflow = new DiffResultItem("s", [LongHunk(12)]);
        Assert.Equal(13, overflow.MeasureHeight(width: 80));
    }

    [Fact]
    public void Patch_ExposedForDriverAssertions()
    {
        var hunks = new[] { Hunk(Line(DiffLineKind.Added, "x")) };
        var item = new DiffResultItem("summary", hunks);

        Assert.Equal("summary", item.Summary);
        Assert.Same(hunks, item.Patch);
    }

    [Fact]
    public void IsExpanded_DefaultsFalse()
    {
        Assert.False(new DiffResultItem("s", [Hunk(Line(DiffLineKind.Added, "x"))]).IsExpanded);
    }

    [Fact]
    public void ToggleExpand_RendersFullPatchWithoutCollapseHint()
    {
        // 13 行 patch（header+12）：折叠 = 预览 10 + 提示 +3（高度 13）；展开 = 全量 13 行（高度 15），
        // 原提示行位置（row12）起接续渲染隐藏行 line 9..11 — 无折叠提示行
        using var screen = new Screen(cols: 40, rows: 2 + 13);
        var item = new DiffResultItem("Edited a.cs", [LongHunk(12)]);
        Assert.Equal(13, item.MeasureHeight(width: 80));

        item.ToggleExpand();

        Assert.True(item.IsExpanded);
        Assert.Equal(15, item.MeasureHeight(width: 80));
        item.Render(screen, new Rect(0, 0, 40, screen.Rows), Theme.Dark);
        Assert.Equal("     line 9", RowText.Of(screen, 12, 0, 11));
        Assert.Equal("     line 10", RowText.Of(screen, 13, 0, 12));
        Assert.Equal("     line 11", RowText.Of(screen, 14, 0, 12));
    }

    [Fact]
    public void ToggleExpand_Twice_ReturnsToCollapsedPreview()
    {
        var item = new DiffResultItem("s", [LongHunk(12)]);

        item.ToggleExpand();
        item.ToggleExpand();

        Assert.False(item.IsExpanded);
        Assert.Equal(13, item.MeasureHeight(width: 80));
    }

    [Fact]
    public void ToggleExpand_WithinPreviewLimit_HeightUnchanged()
    {
        // patch 2 行 ≤ 预览上限 10：展开态无折叠提示，高度不变（展开只对超限 patch 有视觉差异）
        var item = new DiffResultItem("s", [Hunk(Line(DiffLineKind.Added, "x"))]);

        item.ToggleExpand();

        Assert.Equal(4, item.MeasureHeight(width: 80));
    }
}
