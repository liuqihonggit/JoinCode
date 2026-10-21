namespace NativeTui.Tests.Repl;

/// <summary>
/// 流式文本项渲染测试（M8-1 逐字流式 — TASK064）— 对齐基准流式行形态：
/// Messages.tsx 尾部追加 ⎿ 前缀行渲染 StreamingMarkdown（方案 A 先纯文本逐字追加）；
/// ⎿ 前缀整体 dim（对齐 ToolResultItem "  ⎿  " 先例），续行缩进对齐内容列；
/// OnDone 后由 Driver 替换为完整 Markdown/Assistant 项（⏺ 形态 — 替换语义见 ReplBridgeDriverTests）。
/// </summary>
public sealed class StreamingTextItemTests
{
    [Fact]
    public void MeasureHeight_MarginPlusLineCount()
    {
        var item = new StreamingTextItem();
        item.Append("hello world");

        Assert.Equal(2, item.MeasureHeight(40));
    }

    [Fact]
    public void MeasureHeight_GrowsAsTextAppends()
    {
        var item = new StreamingTextItem();
        Assert.Equal(2, item.MeasureHeight(40));

        item.Append("first");
        Assert.Equal(2, item.MeasureHeight(40));

        item.Append("\nsecond line");
        Assert.Equal(3, item.MeasureHeight(40));
    }

    [Fact]
    public void Render_ToolResultIndentPrefixWithDimStyle()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new StreamingTextItem();
        item.Append("streaming text");

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        // row0 margin 留空；row1 = "  ⎿  " dim 前缀 + 默认样式正文（对齐 RenderToolResult 形态）
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  ⎿  ", RowText.Of(screen, 1, 0, 5));
        Assert.Equal(dim, screen[1, 0].StyleId);
        Assert.Equal("streaming text", RowText.Of(screen, 1, 5, 14));
        Assert.Equal(0, screen[1, 5].StyleId);
    }

    [Fact]
    public void Render_MultiLineContinuationAlignsAtContentColumn()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new StreamingTextItem();
        item.Append("line one\nline two");

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("  ⎿  line one", RowText.Of(screen, 1, 0, 13));
        Assert.Equal("      line two", RowText.Of(screen, 2, 0, 14));
    }

    [Fact]
    public void Text_ExposesAccumulatedBuffer()
    {
        var item = new StreamingTextItem();
        item.Append("hello ");
        item.Append("world");

        Assert.Equal("hello world", item.Text);
    }

    [Fact]
    public void Append_EmptyTextKeepsState()
    {
        var item = new StreamingTextItem();
        item.Append("abc");
        item.Append(string.Empty);

        Assert.Equal("abc", item.Text);
        Assert.Equal(2, item.MeasureHeight(40));
    }
}
