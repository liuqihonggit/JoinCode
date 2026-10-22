namespace NativeTui.Tests.Markdown;

/// <summary>
/// DSG043 P1 — MarkdownSegment.Url 链接载体测试：
/// record 位置参数末尾可选（null 默认 — 旧构造调用零破坏 + JSON WhenWritingNull 旧快照零变化）；
/// MarkdigProjector LinkLeaf 投影产 (text, Link style, url) 三元组段。
/// </summary>
public sealed class MarkdownLinkSegmentTests
{
    [Fact]
    public void Segment_Url_Optional_DefaultsNull()
    {
        // 旧两参构造零破坏（Url 默认 null）
        var plain = new MarkdownSegment("text", MarkdownStyle.None);
        Assert.Null(plain.Url);
    }

    [Fact]
    public void Segment_ThreeArg_Construction_CarriesUrl()
    {
        var link = new MarkdownSegment("docs", MarkdownStyle.Link, "https://example.com");
        Assert.Equal("https://example.com", link.Url);
        Assert.Equal(MarkdownStyle.Link, link.Style);
    }

    [Fact]
    public void Segment_ValueEquality_IncludesUrl()
    {
        Assert.Equal(
            new MarkdownSegment("a", MarkdownStyle.Link, "https://x"),
            new MarkdownSegment("a", MarkdownStyle.Link, "https://x"));
        Assert.NotEqual(
            new MarkdownSegment("a", MarkdownStyle.Link, "https://x"),
            new MarkdownSegment("a", MarkdownStyle.Link, "https://y"));
    }

    [Fact]
    public void Segment_Json_RoundTrips_Url()
    {
        var doc = new MarkdownDocument(
        [
            new MarkdownParagraph(
            [
                new MarkdownSegment("see ", MarkdownStyle.None),
                new MarkdownSegment("guide", MarkdownStyle.Link, "https://example.com/guide"),
            ]),
        ]);

        var json = JsonSerializer.Serialize(doc, BridgeJsonContext.Default.MarkdownDocument);
        var round = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.MarkdownDocument);
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(round!.Blocks));
        var linkSeg = para.Segments[1];
        Assert.Equal("guide", linkSeg.Text);
        Assert.Equal(MarkdownStyle.Link, linkSeg.Style);
        Assert.Equal("https://example.com/guide", linkSeg.Url);
    }

    [Fact]
    public void Segment_Json_OldTwoArgForm_Deserializes_UrlNull()
    {
        // 旧引擎 JSON（无 url 字段）反序列化兼容 — Url=null 走纯文本渲染降级
        var json = """{"blocks":[{"type":"paragraph","segments":[{"text":"plain","style":0}]}]}""";
        var round = JsonSerializer.Deserialize(json, BridgeJsonContext.Default.MarkdownDocument);
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(round!.Blocks));
        Assert.Null(para.Segments[0].Url);
    }

    [Fact]
    public void MarkdigProjector_LinkInline_CarriesUrlOnChildSegments()
    {
        // [指南](https://example.com/guide) — 单文字段携带 Url
        var doc = MarkdigProjector.ProjectOptional("[指南](https://example.com/guide)");
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(doc!.Blocks));
        var seg = Assert.Single(para.Segments);
        Assert.Equal("指南", seg.Text);
        Assert.Equal(MarkdownStyle.Link, seg.Style);
        Assert.Equal("https://example.com/guide", seg.Url);
    }

    [Fact]
    public void MarkdigProjector_LinkWithNestedBold_EveryChildCarriesUrl()
    {
        // [**粗体**链接](https://x/y) — 嵌套强调拆两段，每段独立携带同一 Url
        var doc = MarkdigProjector.ProjectOptional("[**粗体**链接](https://x/y)");
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(doc!.Blocks));
        Assert.All(para.Segments, seg => Assert.Equal("https://x/y", seg.Url));
        Assert.Contains(para.Segments, seg => seg.Style.HasFlag(MarkdownStyle.Bold));
    }

    [Fact]
    public void MarkdigProjector_Autolink_CarriesUrl()
    {
        // <https://auto.link> — autolink 文本即 Url 双携带
        var doc = MarkdigProjector.ProjectOptional("<https://auto.link>");
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(doc!.Blocks));
        var seg = Assert.Single(para.Segments);
        Assert.Equal("https://auto.link", seg.Url);
        Assert.Equal(MarkdownStyle.Link, seg.Style);
    }

    [Fact]
    public void MarkdigProjector_MailtoLink_DegradesToPlain_NoUrl()
    {
        // [mail](mailto:a@b.c) — mailto 降级纯文本（基准语义），Url 不携带
        var doc = MarkdigProjector.ProjectOptional("[mail](mailto:a@b.c)");
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(doc!.Blocks));
        var seg = Assert.Single(para.Segments);
        Assert.Equal("mail", seg.Text);
        Assert.Null(seg.Url);
    }

    [Fact]
    public void MarkdigProjector_NonLinkMarkdown_NoUrl()
    {
        // 纯文本 ProjectOptional 返 null（无语法不产文档）— 用强调语法产段但无链接
        var doc = MarkdigProjector.ProjectOptional("*强调*文本");
        Assert.NotNull(doc);
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(doc!.Blocks));
        Assert.All(para.Segments, seg => Assert.Null(seg.Url));
    }
}
