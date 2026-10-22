namespace NativeTui.Tests.Repl;

/// <summary>
/// 流式正文项方案 B 测试（块级增量 Markdown 实时渲染）— stub 投影器驱动（Core 零依赖不引 Markdig，
/// 真投影器测试见 NativeTui.Markdown.Tests）：
/// null 投影器降级纯文本（M8-1 行为回归锁）；稳定段走 MarkdownLayout 布局 + 段样式渲染
///（与 MarkdownMessageItem 同源 — 落定替换视觉无跳变）；稳定/尾间 gap 空行（块间空行语义）；
/// 增长尾纯文本默认样式；未闭合围栏尾 dim；Append 置脏重投影 + 同帧缓存不重投影。
/// </summary>
public sealed class StreamingMarkdownItemTests
{
    /// <summary>stub 投影器 — 委托投影 + 调用计数（脏刷新/缓存断言面）。</summary>
    private sealed class StubProjector(Func<string, StreamingMarkdownProjection> project) : IStreamingMarkdownProjector
    {
        public int Calls { get; private set; }

        public StreamingMarkdownProjection Project(string accumulated)
        {
            Calls++;
            return project(accumulated);
        }
    }

    /// <summary>单段投影 helper — 稳定 1 段 + 尾。</summary>
    private static StreamingMarkdownProjection Projection(string stableText, string tail, bool openFence = false) =>
        new([new MarkdownParagraph([new MarkdownSegment(stableText, MarkdownStyle.None)])], tail, openFence);

    [Fact]
    public void NullProjector_RendersPlainTextRegression()
    {
        // M8-1 行为回归锁 — projector null 纯文本逐字：⎿ 前缀 dim + 默认正文 + 续行 6 空格缩进
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new StreamingTextItem();
        item.Append("plain one\nplain two");

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("  ⎿  plain one", RowText.Of(screen, 1, 0, 14));
        Assert.Equal(dim, screen[1, 0].StyleId);
        Assert.Equal("      plain two", RowText.Of(screen, 2, 0, 15));
        Assert.Equal(0, screen[1, 5].StyleId);
        Assert.Equal(3, item.MeasureHeight(40));
    }

    [Fact]
    public void StableAndTail_RenderedWithGapLine()
    {
        // 稳定段（⎿ 后首行）→ gap 空行（块间空行语义）→ 尾纯文本默认样式
        using var screen = new Screen(cols: 40, rows: 5);
        var projector = new StubProjector(_ => Projection("stable para", "growing tail"));
        var item = new StreamingTextItem(projector);
        item.Append("stable para\n\ngrowing tail");

        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal("  ⎿  stable para", RowText.Of(screen, 1, 0, 16));
        Assert.Equal("      ", RowText.Of(screen, 2, 0, 6));
        Assert.Equal("      growing tail", RowText.Of(screen, 3, 0, 18));
        Assert.Equal(0, screen[3, 6].StyleId);
        Assert.Equal(4, item.MeasureHeight(40));
    }

    [Fact]
    public void StableBlockInlineStyle_AppliedInStream()
    {
        // 稳定段行内样式实时生效 — bold 段渲染 bold 样式（流式期即 Markdown 渲染）
        using var screen = new Screen(cols: 40, rows: 3);
        var projection = new StreamingMarkdownProjection(
            [new MarkdownParagraph([new MarkdownSegment("bold", MarkdownStyle.Bold)])], string.Empty, false);
        var projector = new StubProjector(_ => projection);
        var item = new StreamingTextItem(projector);
        item.Append("**bold**");

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal("  ⎿  bold", RowText.Of(screen, 1, 0, 9));
        Assert.Equal(bold, screen[1, 5].StyleId);
    }

    [Fact]
    public void HeadingBlock_LevelStyleApplied()
    {
        // 稳定标题块 — h1 级别样式由布局层施加（Bold|Italic|Underline），流式渲染同 MarkdownMessageItem
        using var screen = new Screen(cols: 40, rows: 3);
        var projection = new StreamingMarkdownProjection(
            [new MarkdownHeading(1, [new MarkdownSegment("Title", MarkdownStyle.None)])], string.Empty, false);
        var projector = new StubProjector(_ => projection);
        var item = new StreamingTextItem(projector);
        item.Append("# Title");

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var heading = screen.Styles.GetOrAdd(new TextStyle { Bold = true, Italic = true, Underline = true });
        Assert.Equal("  ⎿  Title", RowText.Of(screen, 1, 0, 10));
        Assert.Equal(heading, screen[1, 5].StyleId);
    }

    [Fact]
    public void OpenFenceTail_RenderedDim()
    {
        // 未闭合围栏尾 — dim 提示态（对齐基准未闭合围栏处理）
        using var screen = new Screen(cols: 40, rows: 6);
        var projector = new StubProjector(_ => Projection("intro", "```cs\nvar x", openFence: true));
        var item = new StreamingTextItem(projector);
        item.Append("intro\n\n```cs\nvar x");

        item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("      ```cs", RowText.Of(screen, 3, 0, 11));
        Assert.Equal(dim, screen[3, 6].StyleId);
    }

    [Fact]
    public void MultiLineTail_RenderedLineByLine()
    {
        // 尾多行（增长中多行块）逐行渲染 — 各行从内容列起
        using var screen = new Screen(cols: 40, rows: 5);
        var projector = new StubProjector(_ => new StreamingMarkdownProjection(
            [new MarkdownParagraph([new MarkdownSegment("head", MarkdownStyle.None)])], "l1\nl2", false));
        var item = new StreamingTextItem(projector);
        item.Append("head\n\nl1\nl2");

        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal("  ⎿  head", RowText.Of(screen, 1, 0, 9));
        Assert.Equal("      l1", RowText.Of(screen, 3, 0, 8));
        Assert.Equal("      l2", RowText.Of(screen, 4, 0, 8));
        Assert.Equal(5, item.MeasureHeight(40));
    }

    [Fact]
    public void Append_InvalidatesProjection_SameFrameCaches()
    {
        // Append 置脏 → 下次访问重投影；同宽度重复访问走缓存不重投影（每帧 Measure+Render 只投影一次）
        var projector = new StubProjector(text => new StreamingMarkdownProjection([], text, false));
        var item = new StreamingTextItem(projector);
        item.Append("a");

        _ = item.MeasureHeight(40);
        Assert.Equal(1, projector.Calls);

        item.Append("b");
        _ = item.MeasureHeight(40);
        Assert.Equal(2, projector.Calls);

        _ = item.MeasureHeight(40);
        _ = item.MeasureHeight(40);
        Assert.Equal(2, projector.Calls);
    }

    [Fact]
    public void Append_GrowsHeightWithStableBlocks()
    {
        // 流式推进 — 尾跨过块边界转稳定块后高度增长（稳定布局行 + gap + 新尾行）
        var projector = new StubProjector(accumulated => accumulated.Contains('\n')
            ? Projection("para", "next")
            : new StreamingMarkdownProjection([], accumulated, false));
        var item = new StreamingTextItem(projector);

        item.Append("para");
        Assert.Equal(2, item.MeasureHeight(40)); // 全尾单行

        item.Append("\n\nnext");
        Assert.Equal(4, item.MeasureHeight(40)); // 稳定 1 行 + gap 1 行 + 尾 1 行
    }

    [Fact]
    public void EmptyProjection_SinglePrefixLine()
    {
        // 空投影兜底 — 高度 2（margin + ⎿ 空行），对齐纯文本空缓冲形态
        var projector = new StubProjector(_ => new StreamingMarkdownProjection([], string.Empty, false));
        var item = new StreamingTextItem(projector);

        Assert.Equal(2, item.MeasureHeight(40));
        using var screen = new Screen(cols: 40, rows: 2);
        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);
        Assert.Equal("  ⎿  ", RowText.Of(screen, 1, 0, 5));
    }

    [Fact]
    public void Text_And_ContainsText_ContractUnchanged()
    {
        // M8-1 契约保留 — Text 累积 + transcript 搜索匹配（Driver 替换/定格语义消费）
        var projector = new StubProjector(_ => new StreamingMarkdownProjection([], string.Empty, false));
        var item = new StreamingTextItem(projector);
        item.Append("hello ");
        item.Append("world");

        Assert.Equal("hello world", item.Text);
        Assert.True(item.ContainsText("WORLD"));
        Assert.False(item.ContainsText("missing"));
    }
}
