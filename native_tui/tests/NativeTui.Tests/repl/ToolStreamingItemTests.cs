namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具流式输出项渲染测试（TASK065 P3 — Bash 工具运行中输出实时显示）— 对齐基准 Bash
/// 流式 tail 形态：最近 3 行 dim 输出环形（消息为引擎侧每秒 stdout 快照 → Update 整体替换），
/// 超出 3 行 "… +N lines" 溢出提示行前置（上方被截语义 — 尾部行恒可见），
/// 单行超长截断（200 上限 — 对齐结果摘要截断先例）；首行 "  ⎿  " 前缀 + 续行等宽缩进
///（对齐 ToolResultItem 输出块形态）整行 dim（流态弱化 — tool_result 替换后恢复正式形态）。
/// </summary>
public sealed class ToolStreamingItemTests
{
    /// <summary>超出尾部上限（5 行快照）— 环形保留最近 3 行 + "… +2 lines" 溢出提示行前置。</summary>
    [Fact]
    public void Update_KeepsLastThreeLinesWithOverflowHint()
    {
        var item = new ToolStreamingItem();

        item.Update("line1\nline2\nline3\nline4\nline5");

        Assert.Equal(["… +2 lines", "line3", "line4", "line5"], item.DisplayLines);
        Assert.Equal(2, item.HiddenLines);
    }

    /// <summary>快照内行数不超上限 — 无溢出提示行直出全量（快照本身已是引擎侧最近行）。</summary>
    [Fact]
    public void Update_WithinTailLimit_NoOverflowHint()
    {
        var item = new ToolStreamingItem();

        item.Update("only\ntwo lines");

        Assert.Equal(["only", "two lines"], item.DisplayLines);
        Assert.Equal(0, item.HiddenLines);
    }

    /// <summary>快照整体替换语义 — 第二次 Update 覆盖第一次（引擎发的是当前 stdout 最近行快照非增量）。</summary>
    [Fact]
    public void Update_ReplacesPreviousSnapshotEntirely()
    {
        var item = new ToolStreamingItem();
        item.Update("old1\nold2\nold3\nold4");

        item.Update("new1");

        Assert.Equal(["new1"], item.DisplayLines);
        Assert.Equal(0, item.HiddenLines);
    }

    /// <summary>单行超长截断 — 200 字符上限 + … 尾（对齐 ToolResultSummaryLimit=200 显示截断先例）。</summary>
    [Fact]
    public void Update_TruncatesSingleLongLineToLimit()
    {
        var item = new ToolStreamingItem();
        var longLine = new string('x', 500);

        item.Update(longLine);

        var line = Assert.Single(item.DisplayLines);
        Assert.Equal(200, line.Length);
        Assert.Equal("…", line[^1].ToString());
    }

    /// <summary>空快照清空尾巴 — 高度回落仅 margin（无尾巴零占屏 — 流式项空态不渲染行）。</summary>
    [Fact]
    public void Update_EmptyMessageClearsTail()
    {
        var item = new ToolStreamingItem();
        item.Update("some output");

        item.Update(string.Empty);

        Assert.Empty(item.DisplayLines);
        Assert.Equal(0, item.HiddenLines);
        Assert.Equal(1, item.MeasureHeight(40));
    }

    /// <summary>内容高度 = margin 1 + 显示行数（溢出提示行计入）。</summary>
    [Fact]
    public void MeasureHeight_MarginPlusDisplayLineCount()
    {
        var item = new ToolStreamingItem();
        Assert.Equal(1, item.MeasureHeight(40));

        item.Update("line1\nline2\nline3\nline4\nline5");
        Assert.Equal(5, item.MeasureHeight(40));
    }

    /// <summary>渲染形态 — margin 行留空；首行 "  ⎿  " dim 前缀 + 内容 dim（流态整体弱化）；
    /// 续行 ToolResultIndent 等宽空格缩进（5 列 — 对齐 ToolResultItem 输出续行形态）。</summary>
    [Fact]
    public void Render_DimIndentPrefixWithContinuationIndent()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new ToolStreamingItem();
        item.Update("line1\nline2");

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // row0 margin 留空；row1 = "  ⎿  " dim 前缀 + dim 正文
        Assert.Equal("  ⎿  ", RowText.Of(screen, 1, 0, 5));
        Assert.Equal(dim, screen[1, 0].StyleId);
        Assert.Equal("line1", RowText.Of(screen, 1, 5, 5));
        Assert.Equal(dim, screen[1, 5].StyleId);
        // 续行 5 空格等宽缩进 + dim 正文
        Assert.Equal("     line2", RowText.Of(screen, 2, 0, 10));
        Assert.Equal(dim, screen[2, 5].StyleId);
    }

    /// <summary>溢出提示行渲染 — "… +N lines" 整行 dim 前置（上方被截语义 — 尾部行恒可见）。</summary>
    [Fact]
    public void Render_OverflowHintPrecedesTailLines()
    {
        using var screen = new Screen(cols: 40, rows: 6);
        var item = new ToolStreamingItem();
        item.Update("l1\nl2\nl3\nl4\nl5");

        item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);

        Assert.Equal("  ⎿  … +2 lines", RowText.Of(screen, 1, 0, 15));
        Assert.Equal("     l3", RowText.Of(screen, 2, 0, 7));
        Assert.Equal("     l4", RowText.Of(screen, 3, 0, 7));
        Assert.Equal("     l5", RowText.Of(screen, 4, 0, 7));
    }

    /// <summary>transcript 搜索匹配 — 显示行内容（含溢出提示行）。</summary>
    [Fact]
    public void ContainsText_MatchesDisplayLines()
    {
        var item = new ToolStreamingItem();
        item.Update("compiling module\nlinking");

        Assert.True(item.ContainsText("linking"));
        Assert.True(item.ContainsText("MODULE"));
        Assert.False(item.ContainsText("absent"));
    }
}
