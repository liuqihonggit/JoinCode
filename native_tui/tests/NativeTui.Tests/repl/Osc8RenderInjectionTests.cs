namespace NativeTui.Tests.Repl;

/// <summary>
/// DSG043 P3 — OSC 8 超链接渲染注入测试：Screen 行级链接表（SetLinks/Links/Clear 原子清）
/// → MarkdownMessageItem.Render 布局区间平移写入（BodyIndent 偏移）→ Compose cell 流
/// 区间边界注入 OSC 8 开/闭对（HyperlinkEmitter 降级链：不支持终端零注入）。
/// </summary>
public sealed class Osc8RenderInjectionTests
{
    /// <summary>含链接的 Markdown 文档（单行段落 + 链接段）。</summary>
    private static MarkdownDocument LinkDoc() => new(
    [
        new MarkdownParagraph(
        [
            new MarkdownSegment("see ", MarkdownStyle.None),
            new MarkdownSegment("guide", MarkdownStyle.Link, "https://e.com/g"),
        ]),
    ]);

    [Fact]
    public void Screen_SetLinks_RoundTrips_And_ClearResets()
    {
        using var screen = new Screen(cols: 20, rows: 2);
        Assert.Empty(screen.Links(0));

        screen.SetLinks(0, [new LinkSpan(2, 7, "https://x.io")]);
        var span = Assert.Single(screen.Links(0));
        Assert.Equal(2, span.StartCol);
        Assert.Equal("https://x.io", span.Url);

        // Clear 原子清（帧重画防残留旧链接）
        screen.Clear();
        Assert.Empty(screen.Links(0));
        Assert.Empty(screen.Links(1));
    }

    [Fact]
    public void MarkdownItem_Render_WritesLinks_WithIndentOffset()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new MarkdownMessageItem(LinkDoc(), thinkingDurationMs: 0);
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        // margin 1 + 圆点行首 → 布局行落在 row1；区间平移 BodyIndentCols=2：
        // 布局区间 [4,9) → Screen [6,11)
        var span = Assert.Single(screen.Links(1));
        Assert.Equal(6, span.StartCol);
        Assert.Equal(11, span.EndCol);
        Assert.Equal("https://e.com/g", span.Url);
    }

    /// <summary>首帧 Compose 输出含 OSC 8 对：链接文本前后开/闭序列（ST 终止形态）。</summary>
    [Fact]
    public void InlineComposer_Compose_FirstFrame_InjectsOsc8Pair()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        new MarkdownMessageItem(LinkDoc(), thinkingDurationMs: 0).Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);
        screen.SetLinks(1, [new LinkSpan(6, 11, "https://e.com/g")]);

        var composer = new InlineComposer(hyperlinksSupported: true);
        var output = composer.Compose(prev: null, screen, viewportRows: 4,
            prevCursor: new CursorPos(0, 0), frameEndCursor: new CursorPos(0, 4), out _);

        Assert.Contains("\x1b]8;;https://e.com/g\x1b\\", output);
        Assert.Contains("guide\x1b]8;;\x1b\\", output);
    }

    /// <summary>无链接行零注入（无序列泄漏 — 干净行输出不含 OSC 8）。</summary>
    [Fact]
    public void InlineComposer_Compose_NoLinks_ZeroInjection()
    {
        using var screen = new Screen(cols: 40, rows: 2);
        screen.WriteText(0, 0, "plain line", 0, 40);

        var composer = new InlineComposer();
        var output = composer.Compose(prev: null, screen, viewportRows: 2,
            prevCursor: new CursorPos(0, 0), frameEndCursor: new CursorPos(0, 2), out _);

        Assert.DoesNotContain("\x1b]8;", output);
    }

    /// <summary>降级链 — 终端不支持超链接时 Compose 零注入（纯文本降级，T4 探测消费）。</summary>
    [Fact]
    public void InlineComposer_Compose_UnsupportedTerminal_NoInjection()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        new MarkdownMessageItem(LinkDoc(), thinkingDurationMs: 0).Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        var composer = new InlineComposer(hyperlinksSupported: false);
        var output = composer.Compose(prev: null, screen, viewportRows: 4,
            prevCursor: new CursorPos(0, 0), frameEndCursor: new CursorPos(0, 4), out _);

        Assert.DoesNotContain("\x1b]8;", output);
    }

    /// <summary>Link 样式视觉 — Suggestion 前景 + 下划线（基准 blue 超链接惯例的 theme 映射）。</summary>
    [Fact]
    public void MapMarkdownStyle_Link_SuggestionForegroundUnderline()
    {
        var style = MessageRenderer.MapMarkdownStyle(MarkdownStyle.Link, Theme.Dark);
        Assert.True(style.Underline);
        Assert.Equal(Theme.Dark.Suggestion, style.Foreground);
    }

    /// <summary>差分路径（prev 非 null）重写链接行 — 同样注入（增量帧 OSC 8 不丢）。</summary>
    [Fact]
    public void InlineComposer_Compose_DiffFrame_InjectsOsc8Pair()
    {
        using var prev = new Screen(cols: 40, rows: 4);
        using var next = new Screen(cols: 40, rows: 4);
        new MarkdownMessageItem(LinkDoc(), thinkingDurationMs: 0).Render(next, new Rect(0, 0, 40, 4), Theme.Dark);

        var composer = new InlineComposer(hyperlinksSupported: true);
        var output = composer.Compose(prev, next, viewportRows: 4,
            prevCursor: new CursorPos(0, 4), frameEndCursor: new CursorPos(0, 4), out _);

        Assert.Contains("\x1b]8;;https://e.com/g\x1b\\", output);
    }
}
