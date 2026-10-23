namespace NativeTui.Tests.Markdown;

/// <summary>
/// DSG043 P2 — 布局折行链接区间表测试：MarkdownSegment.Url 穿透 Tokenize/MergeSegments
///（合并键 Style+Url 防不同链接段错误合并）+ LayoutLines.Add 行级自动提取 LinkSpan
///（物理列累计，含前缀偏移天然正确）+ 折行拆分每行区间独立。
/// </summary>
public sealed class MarkdownLinkSpanTests
{
    /// <summary>布局产物 — 段落单行单链接：区间 [前缀+空格起, 止) 携带 Url。</summary>
    [Fact]
    public void Layout_SingleLineLink_SpanCarriesUrl()
    {
        var doc = new MarkdownDocument([
            new MarkdownParagraph([
                new MarkdownSegment("see ", MarkdownStyle.None),
                new MarkdownSegment("guide", MarkdownStyle.Link, "https://e.com/g"),
            ]),
        ]);

        var result = MarkdownLayout.LayoutWithHighlights(doc, width: 40, highlighter: null);
        var spans = Assert.Single(result.Links);
        var span = Assert.Single(spans);
        Assert.Equal(4, span.StartCol);
        Assert.Equal(9, span.EndCol);
        Assert.Equal("https://e.com/g", span.Url);
    }

    /// <summary>折行拆分 — 链接 token 跨行硬断后每行区间独立（区间只覆盖本行物理列）。</summary>
    [Fact]
    public void Layout_LinkAcrossWrap_EachLineSpanIndependent()
    {
        // 长链接文本在窄宽下折行：链接段被拆到多行，各行的 Links 各自独立区间
        var linkText = new string('L', 30);
        var doc = new MarkdownDocument([
            new MarkdownParagraph([
                new MarkdownSegment(linkText, MarkdownStyle.Link, "https://x.io/long"),
            ]),
        ]);

        var result = MarkdownLayout.LayoutWithHighlights(doc, width: 12, highlighter: null);
        Assert.True(result.Lines.Count >= 2, $"窄宽 12 列 30 字符应折行（实际 {result.Lines.Count} 行）");

        // 每行都有区间且 Url 一致；区间端点在本行范围内
        for (var i = 0; i < result.Lines.Count; i++)
        {
            var spans = result.Links[i];
            if (spans.Count > 0)
            {
                var span = Assert.Single(spans);
                Assert.Equal("https://x.io/long", span.Url);
                Assert.True(span.StartCol < span.EndCol && span.EndCol <= 12, $"行 {i} 区间越界 [{span.StartCol},{span.EndCol})");
            }
        }
    }

    /// <summary>无链接文档 — Links 等长全空表（与 Lines 对齐，P3 消费零判断成本）。</summary>
    [Fact]
    public void Layout_NoLinks_EmptySpansPerLine()
    {
        var doc = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("plain text only", MarkdownStyle.None)]),
        ]);

        var result = MarkdownLayout.LayoutWithHighlights(doc, width: 40, highlighter: null);
        Assert.Equal(result.Lines.Count, result.Links.Count);
        Assert.All(result.Links, spans => Assert.Empty(spans));
    }

    /// <summary>两相邻不同 Url 链接段不合并（MergeSegments 合并键含 Url — 防丢链接归属）。</summary>
    [Fact]
    public void Layout_AdjacentDifferentUrls_NotMerged()
    {
        var doc = new MarkdownDocument([
            new MarkdownParagraph([
                new MarkdownSegment("a", MarkdownStyle.Link, "https://a.io"),
                new MarkdownSegment("b", MarkdownStyle.Link, "https://b.io"),
            ]),
        ]);

        var result = MarkdownLayout.LayoutWithHighlights(doc, width: 40, highlighter: null);
        var line = Assert.IsType<MarkdownParagraph>(doc.Blocks[0]);
        var spans = result.Links[0];
        Assert.Equal(2, spans.Count);
        Assert.Equal("https://a.io", spans[0].Url);
        Assert.Equal("https://b.io", spans[1].Url);
        Assert.Equal(0, spans[0].StartCol);
        Assert.Equal(1, spans[0].EndCol);
        Assert.Equal(1, spans[1].StartCol);
        Assert.Equal(2, spans[1].EndCol);
    }

    /// <summary>标题/列表共用 AppendParagraph 路径 — 标题行链接区间同样提取。</summary>
    [Fact]
    public void Layout_HeadingLink_SpanExtracted()
    {
        var doc = new MarkdownDocument([
            new MarkdownHeading(2, [
                new MarkdownSegment("title ", MarkdownStyle.Bold),
                new MarkdownSegment("link", MarkdownStyle.Bold | MarkdownStyle.Link, "https://h.io"),
            ]),
        ]);

        var result = MarkdownLayout.LayoutWithHighlights(doc, width: 40, highlighter: null);
        var spans = result.Links[0];
        var span = Assert.Single(spans);
        Assert.Equal("https://h.io", span.Url);
        // 标题语义并入样式 flags 无文本前缀 — "title " 6 列后链接 6 起
        Assert.Equal(6, span.StartCol);
        Assert.Equal(10, span.EndCol);
    }

    /// <summary>代码行（AddCode）恒无链接 — 代码块 Url 不在行内文本语义内。</summary>
    [Fact]
    public void Layout_CodeBlock_EmptySpans()
    {
        var doc = new MarkdownDocument([
            new MarkdownCode("csharp", "var x = 1;"),
        ]);

        var result = MarkdownLayout.LayoutWithHighlights(doc, width: 40, highlighter: null);
        Assert.Equal(result.Lines.Count, result.Links.Count);
        Assert.All(result.Links, spans => Assert.Empty(spans));
    }
}
