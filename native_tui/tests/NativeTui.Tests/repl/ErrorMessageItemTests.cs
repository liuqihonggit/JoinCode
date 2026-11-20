namespace NativeTui.Tests.Repl;

/// <summary>
/// 错误消息项渲染测试（TASK069-E4 — 对齐基准 AssistantTextMessage MAX_API_ERROR_CHARS=1000）：
/// 超 1000 字符截断 + "…" + dim `(ctrl+o 展开)` 引导；全文仍可搜索/进 transcript；
/// 含换行时逐行渲染（续行 2 列缩进）— 修复 '\n' 被零宽规则吞掉挤成一行的缺陷。
/// </summary>
public sealed class ErrorMessageItemTests
{
    private static string LongText(int length) => new('x', length);

    [Fact]
    public void ShortText_SingleRedRow()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new ErrorMessageItem("回合执行失败：boom");

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal("● 回合执行失败：boom", RowText.Of(screen, 1, 0, 20));
        Assert.Equal(error, screen[1, 0].StyleId);
    }

    [Fact]
    public void MultiLineError_RendersRowsInsteadOfGlueing()
    {
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new ErrorMessageItem("first line\nsecond line");

        Assert.Equal(3, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal("● first line", RowText.Of(screen, 1, 0, 12));
        Assert.Equal("  second line", RowText.Of(screen, 2, 0, 13));
        Assert.Equal(error, screen[2, 2].StyleId);
    }

    [Fact]
    public void LongText_TruncatedWithExpandHint()
    {
        using var screen = new Screen(cols: 1010, rows: 4);
        var item = new ErrorMessageItem(LongText(1001));

        Assert.True(item.IsTruncated);
        Assert.Equal(3, item.MeasureHeight(1010)); // margin + 正文 1 + 引导 1
        item.Render(screen, new Rect(0, 0, 1010, 4), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  (ctrl+o 展开)", RowText.Of(screen, 2, 0, 15));
        Assert.Equal(dim, screen[2, 2].StyleId);
        // 正文裁到 1000 字符（● 后 2 列起），末位是省略号
        Assert.Equal(new Rune('…'), screen[1, 1002].Value);
    }

    [Fact]
    public void ExactlyAtLimit_NotTruncated()
    {
        var item = new ErrorMessageItem(LongText(1000));

        Assert.False(item.IsTruncated);
        Assert.Equal(2, item.MeasureHeight(40));
    }

    [Fact]
    public void ContainsText_SearchesFullTextBeyondLimit()
    {
        var item = new ErrorMessageItem(LongText(1500) + "TAIL-MARKER");

        Assert.True(item.ContainsText("TAIL-MARKER"));
    }

    [Fact]
    public void Transcript_ProjectsFullTextWithoutTruncation()
    {
        var text = LongText(1500) + "TAIL-MARKER";
        var item = new ErrorMessageItem(text);

        var lines = TranscriptLines.Project(item).ToList();

        Assert.Single(lines);
        Assert.EndsWith("TAIL-MARKER", lines[0], StringComparison.Ordinal);
    }
}
