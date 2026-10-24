// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Tests.Markdown;

/// <summary>
/// 代码高亮 Core 接线测试（M7-5）— 布局产物高亮上下文（普通行 null / 代码行色段 / 前缀偏移 / 降级 null）
/// + MarkdownMessageItem 渲染覆写（token 色段 → cell 前景）+ Driver 注入链（AssistantMessage → 消息项）。
/// stub 高亮器固定色值（不依赖 TextMateSharp 卫星 — Core 纯视图断言）。
/// </summary>
public sealed class MarkdownHighlightTests
{
    /// <summary>stub 高亮器 — 固定三段色（红/绿/蓝各 3 字符）支持 csharp，其余语言返回 null。</summary>
    private sealed class StubHighlighter : ICodeHighlighter
    {
        public IReadOnlyList<IReadOnlyList<HighlightSpan>>? Highlight(string language, IReadOnlyList<string> lines) =>
            language == "csharp"
                ? lines.Select(line => (IReadOnlyList<HighlightSpan>)new[]
                  {
                      new HighlightSpan(0, 3, 255, 0, 0),
                      new HighlightSpan(3, 3, 0, 255, 0),
                      new HighlightSpan(6, Math.Max(0, line.Length - 6), 0, 0, 255),
                  }).ToArray()
                : null;
    }

    /// <summary>构造含代码块的文档。</summary>
    private static MarkdownDocument DocWithCode(string language, string code) =>
        new([new MarkdownCode(language, code)]);

    [Fact]
    public void Layout_CodeLinesCarrySpans_NormalLinesNull()
    {
        var result = MarkdownLayout.LayoutWithHighlights(
            DocWithCode("csharp", "var x = 1;\nvar y = 2;"),
            width: 80,
            new StubHighlighter());

        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Highlights, highlight => Assert.NotNull(highlight));
        Assert.Equal(3, result.Highlights[0]!.Spans.Count);
    }

    [Fact]
    public void Layout_SpanStartOffsetByQuotePrefix()
    {
        // 引用内代码块：前缀 "▎ " 宽 2 → 色段 Start 平移 +2
        var doc = new MarkdownDocument(
        [
            new MarkdownQuote([new MarkdownCode("csharp", "abcdef")]),
        ]);

        var result = MarkdownLayout.LayoutWithHighlights(doc, width: 80, new StubHighlighter());

        var spans = result.Highlights[0]!.Spans;
        Assert.Equal(2, spans[0].Start);
        Assert.Equal(5, spans[1].Start);
    }

    [Fact]
    public void Layout_UnsupportedLanguage_DegradesToNoHighlight()
    {
        var result = MarkdownLayout.LayoutWithHighlights(
            DocWithCode("no-such-lang", "code"),
            width: 80,
            new StubHighlighter());

        Assert.All(result.Highlights, Assert.Null);
    }

    [Fact]
    public void Layout_NoHighlighter_CodeLinesPureText()
    {
        var result = MarkdownLayout.LayoutWithHighlights(DocWithCode("csharp", "var x = 1;"), width: 80, null);

        Assert.All(result.Highlights, Assert.Null);
    }

    [Fact]
    public void Render_CodeLineCellsColoredBySpan()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new MarkdownMessageItem(DocWithCode("csharp", "var x = 1;"), new StubHighlighter());

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        // row1 = margin 后首行：● 占 col0-1，代码从 col2 起
        // stub：col2-4 红(255,0,0)、col5-7 绿(0,255,0)、col8+ 蓝(0,0,255)
        var red = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(255, 0, 0) });
        var green = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(0, 255, 0) });
        var blue = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(0, 0, 255) });
        Assert.Equal(red, screen[1, 2].StyleId);
        Assert.Equal(red, screen[1, 4].StyleId);
        Assert.Equal(green, screen[1, 5].StyleId);
        Assert.Equal(green, screen[1, 7].StyleId);
        Assert.Equal(blue, screen[1, 8].StyleId);
        Assert.Equal(new Rune('v'), screen[1, 2].Value);
        Assert.Equal(new Rune('='), screen[1, 8].Value);
    }

    [Fact]
    public void Render_WithoutHighlighter_CodeCellsDefaultStyle()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new MarkdownMessageItem(DocWithCode("csharp", "var x = 1;"));

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal(0, screen[1, 2].StyleId);
        Assert.Equal(0, screen[1, 8].StyleId);
    }

    [Fact]
    public void Render_LayoutCachedAcrossFrames()
    {
        // 布局缓存 — 同宽度二次渲染不重算高亮（stub 计数验证）
        var calls = 0;
        var counting = new CallbackHighlighter(() => calls++);
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new MarkdownMessageItem(DocWithCode("csharp", "var x = 1;"), counting);

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal(1, calls);
    }

    /// <summary>计数 stub（每次 Highlight 调用回调）。</summary>
    private sealed class CallbackHighlighter(Action onHighlight) : ICodeHighlighter
    {
        public IReadOnlyList<IReadOnlyList<HighlightSpan>>? Highlight(string language, IReadOnlyList<string> lines)
        {
            onHighlight();
            return new[] { (IReadOnlyList<HighlightSpan>)Array.Empty<HighlightSpan>() }.Concat(
                lines.Skip(1).Select(_ => (IReadOnlyList<HighlightSpan>)Array.Empty<HighlightSpan>())).ToArray();
        }
    }

    [Fact]
    public void Driver_PassesHighlighterToMarkdownMessageItem()
    {
        // 注入链：Driver 构造注入 → AssistantMessage(markdown) → 视图渲染出现色段
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder");
        var driver = new ReplBridgeDriver(view, spinner, banner, codeHighlighter: new StubHighlighter());
        view.Messages.Add(banner);
        view.Messages.Add(spinner);

        driver.Apply(new BridgeEvent.AssistantMessage("# 演示", DocWithCode("csharp", "var x = 1;")));

        var item = Assert.IsType<MarkdownMessageItem>(view.Messages.Items[^2]);
        using var screen = new Screen(cols: 40, rows: 4);
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);
        var red = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(255, 0, 0) });
        Assert.Equal(red, screen[1, 2].StyleId);
    }
}
