namespace NativeTui.Tests.Repl;

/// <summary>
/// 注入提示词折叠项测试（TASK145 — 每轮一条）：摘要行渲染（轮次 + 字符数 + tokenNote 回填）/
/// 唯一命中标识（多条按轮次区分点击目标）/ 展开内容为本轮系统提示词全文。
/// </summary>
public sealed class PromptContextItemTests
{
    private static string RowTextOf(Screen screen, int row, int width = 140) =>
        RowText.Of(screen, row, 0, width).TrimEnd();

    [Fact]
    public void RendersRoundAndCharsAndClickHint()
    {
        var item = new PromptContextItem(round: 2, promptChars: 50382, content: "提示词正文");
        using var screen = new Screen(cols: 140, rows: 3);

        Assert.Equal(1, item.MeasureHeight(140));
        item.Render(screen, new Rect(0, 0, 140, 3), Theme.Dark);

        var row = RowTextOf(screen, 0);
        Assert.Contains("📎 第 2 轮注入", row);
        Assert.Contains("50382 字符", row);
        Assert.Contains("点击展开", row);
        Assert.Equal("📎 第 2 轮注入 · 提示词 50382 字符", item.Summary);
    }

    [Fact]
    public void TokenNote_Renders_WhenPromptTokensFilled()
    {
        var item = new PromptContextItem(round: 1, promptChars: 12307, content: "提示词正文") { PromptTokens = 4200 };
        using var screen = new Screen(cols: 140, rows: 3);

        item.Render(screen, new Rect(0, 0, 140, 3), Theme.Dark);

        Assert.Contains("约 4200 tokens", RowTextOf(screen, 0));
    }

    [Fact]
    public void HitTestId_IsUniquePerRound_AndPrefixMatched()
    {
        var first = new PromptContextItem(round: 1, promptChars: 10, content: "一");
        var second = new PromptContextItem(round: 2, promptChars: 20, content: "二");

        Assert.NotEqual(first.HitTestId, second.HitTestId);
        Assert.True(PromptContextItem.IsPromptHit(first.HitTestId));
        Assert.True(PromptContextItem.IsPromptHit(second.HitTestId));
        Assert.False(PromptContextItem.IsPromptHit("tool-result:1"));
        Assert.False(PromptContextItem.IsPromptHit(null));
    }

    [Fact]
    public void ExpandedContent_IsThisRoundPromptBody()
    {
        var item = new PromptContextItem(round: 3, promptChars: 5, content: "系统提示词");

        Assert.Equal("系统提示词", item.ExpandedContent);
        Assert.False(item.Expanded);
    }
}