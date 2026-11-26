namespace NativeTui.Tests.Repl;

/// <summary>
/// 本地斜杠命令输出含多行时（如 /login 的"不支持的提供商"两行提示），TUI 必须逐行渲染。
/// Screen.WriteText 跳过零宽字符（\n），此前 RenderAssistant 把整段文本一次写入单行，
/// 换行被吞掉 → 显示为 "第一行第二行" 粘连。
/// </summary>
public sealed class AssistantMultilineRenderTests
{
    /// <summary>两行正文 = margin 1 + 正文 2 行。</summary>
    [Fact]
    public void MeasureHeight_MultilineText_CountsEachLine()
    {
        var item = new AssistantMessageItem("第一行\n第二行");

        Assert.Equal(3, item.MeasureHeight(40));
    }

    /// <summary>首行 ● 前缀，续行与正文列对齐，两行独立可见。</summary>
    [Fact]
    public void Render_MultilineText_PutsEachLineOnOwnRow()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new AssistantMessageItem("第一行\n第二行");

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal($"{Figures.MessageBullet} 第一行", RowText.Of(screen, 1, 0, 7));
        Assert.Equal("  第二行", RowText.Of(screen, 2, 0, 7));
    }

    /// <summary>超宽行必须在正文列内软折行：高度按折后行数算（正文宽 = cols - 2 前缀列）。</summary>
    [Fact]
    public void MeasureHeight_LongLine_WrapsByContentWidth()
    {
        var item = new AssistantMessageItem(new string('a', 60));

        // cols 40 → 正文可用 38 列 → 60 字符折成 2 行 → 高度 1 + 2
        Assert.Equal(3, item.MeasureHeight(40));
    }

    /// <summary>超宽行渲染不得在右缘被裁：首行填满正文列，余下字符续到下一行。</summary>
    [Fact]
    public void Render_LongLine_WrapsInsteadOfClipping()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new AssistantMessageItem(new string('a', 60));

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal(new string('a', 38), RowText.Of(screen, 1, 2, 38));
        Assert.Equal(new string('a', 22), RowText.Of(screen, 2, 2, 22));
    }

    /// <summary>英文长句按空白折行，单词不被腰斩（与详情页同一套断行策略）。</summary>
    [Fact]
    public void Render_LongSentence_BreaksAtSpace()
    {
        using var screen = new Screen(cols: 24, rows: 6);
        var item = new AssistantMessageItem("alpha beta gamma delta epsilon zeta");

        item.Render(screen, new Rect(0, 0, 24, 6), Theme.Dark);

        var joined = string.Join(" ", Enumerable.Range(1, 5)
            .Select(row => RowText.Of(screen, row, 2, 22).TrimEnd())
            .Where(line => line.Length > 0));
        Assert.Contains("epsilon", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("epsi lone", joined, StringComparison.Ordinal);
    }

    /// <summary>同一段文本在更窄宽度下必须变高（resize 后按新宽度重排 — ScrollBox 每帧重测高度）。</summary>
    [Fact]
    public void MeasureHeight_NarrowerWidth_GrowsHeight()
    {
        var item = new AssistantMessageItem(new string('a', 60));

        Assert.True(item.MeasureHeight(20) > item.MeasureHeight(40));
    }

    /// <summary>零回归：单行消息高度与渲染位置保持原样。</summary>
    [Fact]
    public void Render_SingleLine_Unchanged()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new AssistantMessageItem("answer");

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);
        Assert.Equal($"{Figures.MessageBullet} answer", RowText.Of(screen, 1, 0, 8));
    }
}
