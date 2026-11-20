namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具错误结果形态测试（TASK069-M5 — 对齐基准 FallbackToolUseErrorMessage / MessageResponse）：
/// 错误内容整体走 error 色块（首行 ⎿ 前缀 dim、续行 5 列缩进），最多 10 行 + `… +N lines (ctrl+o to see all)`
/// 展开引导；摘要行与输出折叠块让位（错误块即全文，不叠两处内容）；非错误形态不受影响。
/// </summary>
public sealed class ToolResultItemErrorTests
{
    [Fact]
    public void SingleLineError_RendersErrorColorWithPrefix()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new ToolResultItem("boom", null, null, 0, isError: true);

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  ⎿  Error: boom", RowText.Of(screen, 1, 0, 16));
        Assert.Equal(dim, screen[1, 0].StyleId);
        Assert.Equal(error, screen[1, 5].StyleId);
    }

    [Fact]
    public void MultiLineError_ContinuesWithIndentInsteadOfGlueing()
    {
        // 修复点：此前多行错误摘要被 WriteText 跳过 '\n' 挤成一行（"Error: line1line2"）
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new ToolResultItem("line1\nline2", null, "line1\nline2", hiddenLines: 0, isError: true);

        Assert.Equal(3, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal("  ⎿  Error: line1", RowText.Of(screen, 1, 0, 17));
        Assert.Equal("     line2", RowText.Of(screen, 2, 0, 10));
        Assert.Equal(error, screen[2, 5].StyleId);
        // 错误块即全文 — 不再渲染第二份输出预览
        Assert.Equal(new Rune(' '), screen[3, 0].Value);
    }

    [Fact]
    public void ErrorBlock_CapsAtTenLinesWithHint()
    {
        var raw = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"line{i}"));
        using var screen = new Screen(cols: 40, rows: 14);
        var item = new ToolResultItem(raw, null, raw, hiddenLines: 9, isError: true);

        Assert.Equal(12, item.MeasureHeight(40)); // margin 1 + 10 行 + 提示 1
        item.Render(screen, new Rect(0, 0, 40, 14), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  ⎿  Error: line1", RowText.Of(screen, 1, 0, 17));
        Assert.Equal("     line10", RowText.Of(screen, 10, 0, 11));
        var hint = $"     {ToolFoldingPolicy.ErrorHiddenLinesHint(2)}";
        Assert.Equal(hint, RowText.Of(screen, 11, 0, hint.Length));
        Assert.Equal(dim, screen[11, 5].StyleId);
    }

    [Fact]
    public void ExpandedErrorBlock_ShowsAllLinesWithoutHint()
    {
        var raw = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"line{i}"));
        using var screen = new Screen(cols: 40, rows: 16);
        var item = new ToolResultItem(raw, null, raw, hiddenLines: 9, isError: true);
        item.SetExpanded(true);

        Assert.Equal(13, item.MeasureHeight(40)); // margin 1 + 12 行
        item.Render(screen, new Rect(0, 0, 40, 16), Theme.Dark);

        Assert.Equal("     line12", RowText.Of(screen, 12, 0, 11));
        Assert.Equal(new Rune(' '), screen[13, 0].Value);
    }

    [Fact]
    public void ErrorWithExistingPrefix_NotDuplicated()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new ToolResultItem("Error: already marked", null, null, 0, isError: true);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("  ⎿  Error: already marked", RowText.Of(screen, 1, 0, 26));
    }

    [Fact]
    public void NonErrorItem_KeepsDefaultColorAndNoPrefix()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new ToolResultItem("Done", null, null, 0);

        Assert.False(item.IsError);
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("  ⎿  Done", RowText.Of(screen, 1, 0, 9));
        Assert.Equal(0, screen[1, 5].StyleId);
    }

    [Fact]
    public void Transcript_ErrorItem_ProjectsSingleErrorBlock()
    {
        // 与渲染同源 — 不再同时投影摘要行和输出预览（两份重复内容）
        var item = new ToolResultItem("line1\nline2", null, "line1\nline2", hiddenLines: 0, isError: true);

        var lines = TranscriptLines.Project(item).ToList();

        Assert.Equal(["  ⎿  Error: line1", "     line2"], lines);
    }

    [Fact]
    public void ContainsText_ErrorItem_MatchesFullErrorContent()
    {
        var item = new ToolResultItem("boom", null, "boom details\nstack trace", hiddenLines: 0, isError: true);

        Assert.True(item.ContainsText("stack trace"));
    }
}
