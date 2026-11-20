namespace NativeTui.Tests.Repl;

/// <summary>
/// 流式文本项渲染测试（M8-1 逐字流式 — TASK064）— 对齐基准流式行形态：
/// Messages.tsx 尾部追加 ⎿ 前缀行渲染 StreamingMarkdown；⎿ 前缀整体 dim
///（对齐 ToolResultItem "  ⎿  " 先例），续行缩进对齐内容列；
/// OnDone 后由 Driver 替换为完整 Markdown/Assistant 项（⏺ 形态 — 替换语义见 ReplBridgeDriverTests）。
/// TASK069-S1 — 上屏粒度对齐基准：只显示到最后一个换行为止的**完整行**
///（REPL.tsx `streamingText.substring(0, lastIndexOf('\n') + 1)`，注释明写
/// "line-by-line, not char-by-char"）——未完成的尾行不显示，`Text` 仍保留全文。
/// </summary>
public sealed class StreamingTextItemTests
{
    [Fact]
    public void MeasureHeight_NoNewlineMeansNothingVisible()
    {
        var item = new StreamingTextItem();
        item.Append("hello world");

        // 无换行 = 没有任何完整行 → 不占位（基准 visibleStreamingText 为 null 时不渲染）
        Assert.Equal(0, item.MeasureHeight(40));
    }

    [Fact]
    public void MeasureHeight_CountsOnlyCompleteLines()
    {
        var item = new StreamingTextItem();
        Assert.Equal(0, item.MeasureHeight(40));

        item.Append("first");
        Assert.Equal(0, item.MeasureHeight(40));

        // 换行到达后 first 才是完整行：margin 1 + 1 行
        item.Append("\nsecond line");
        Assert.Equal(2, item.MeasureHeight(40));

        item.Append("\nthird");
        Assert.Equal(3, item.MeasureHeight(40));
    }

    [Fact]
    public void MeasureHeight_TrailingNewlineDoesNotAddBlankLine()
    {
        var item = new StreamingTextItem();
        item.Append("only line\n");

        // "only line\n" = 1 完整行；末尾换行不额外产生空行
        Assert.Equal(2, item.MeasureHeight(40));
    }

    [Fact]
    public void MeasureHeight_BlankLineBetweenCompleteLinesIsKept()
    {
        var item = new StreamingTextItem();
        item.Append("a\n\nb\n");

        // a + 空行 + b = 3 行；margin 1
        Assert.Equal(4, item.MeasureHeight(40));
    }

    [Fact]
    public void Render_ToolResultIndentPrefixWithDimStyle()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new StreamingTextItem();
        item.Append("streaming text\n");

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        // row0 margin 留空；row1 = "  ⎿  " dim 前缀 + 默认样式正文（对齐 RenderToolResult 形态）
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  ⎿  ", RowText.Of(screen, 1, 0, 5));
        Assert.Equal(dim, screen[1, 0].StyleId);
        Assert.Equal("streaming text", RowText.Of(screen, 1, 5, 14));
        Assert.Equal(0, screen[1, 5].StyleId);
    }

    [Fact]
    public void Render_HidesIncompleteTrailingLine()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new StreamingTextItem();
        item.Append("complete line\nhalf writ");

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("  ⎿  complete line", RowText.Of(screen, 1, 0, 18));
        // 未完成行不渲染（row2 保持空白）
        Assert.Equal(string.Empty, RowText.Of(screen, 2, 0, 20).TrimEnd());
        Assert.Equal(2, item.MeasureHeight(40));
    }

    [Fact]
    public void Render_NothingVisibleBeforeFirstNewline()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new StreamingTextItem();
        item.Append("partial");

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(string.Empty, RowText.Of(screen, row, 0, 20).TrimEnd());
        }
    }

    [Fact]
    public void Render_MultiLineContinuationAlignsAtContentColumn()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new StreamingTextItem();
        item.Append("line one\nline two\n");

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("  ⎿  line one", RowText.Of(screen, 1, 0, 13));
        Assert.Equal("      line two", RowText.Of(screen, 2, 0, 14));
    }

    [Fact]
    public void Text_ExposesFullAccumulatedBuffer()
    {
        var item = new StreamingTextItem();
        item.Append("hello ");
        item.Append("world");

        // 显示截断不影响 Text：落定替换与 transcript 长度按全文
        Assert.Equal("hello world", item.Text);
        Assert.Equal(0, item.MeasureHeight(40));
    }

    [Fact]
    public void Append_EmptyTextKeepsState()
    {
        var item = new StreamingTextItem();
        item.Append("abc");
        item.Append(string.Empty);

        Assert.Equal("abc", item.Text);
        Assert.Equal(0, item.MeasureHeight(40));
    }

    [Fact]
    public void VisibilityRecoversWhenNewlineArrives()
    {
        var item = new StreamingTextItem();
        item.Append("abc");
        Assert.Equal(0, item.MeasureHeight(40));

        item.Append("def");
        Assert.Equal(0, item.MeasureHeight(40));

        item.Append("\n");
        Assert.Equal(2, item.MeasureHeight(40));
    }
}
