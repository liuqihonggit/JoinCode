namespace NativeTui.Tests.Markdown;

/// <summary>
/// DSG043 尾批 — issue 引用链接化文档级展开测试：IssueLinkExpander.ExpandDocument
/// 消费 IssueReferenceExpander 扫描结果把段落/标题纯文本段拆为三段
///（前文原样式/引用子串 Link+GitHub Url/后文原样式，多实例循环拆分）；
/// 已是链接段（Url 非空）不二次处理；defaultOwnerRepo null 时短形态不拆（Expander 契约）。
/// </summary>
public sealed class IssueLinkExpanderTests
{
    [Fact]
    public void Expand_ParagraphIssueReference_SplitsIntoThreeSegments()
    {
        var doc = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("see liuqihonggit/JoinCode#472 landed", MarkdownStyle.None)]),
        ]);

        var expanded = IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: null);
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(expanded.Blocks));
        Assert.Equal(3, para.Segments.Count);
        Assert.Equal(("see ", MarkdownStyle.None, (string?)null), (para.Segments[0].Text, para.Segments[0].Style, para.Segments[0].Url));
        Assert.Equal(("liuqihonggit/JoinCode#472", MarkdownStyle.Link, "https://github.com/liuqihonggit/JoinCode/issues/472"),
            (para.Segments[1].Text, para.Segments[1].Style, para.Segments[1].Url));
        Assert.Equal((" landed", MarkdownStyle.None, (string?)null), (para.Segments[2].Text, para.Segments[2].Style, para.Segments[2].Url));
    }

    [Fact]
    public void Expand_ShortForm_WithContextOwnerRepo()
    {
        var doc = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("fix in #471 done", MarkdownStyle.None)]),
        ]);

        var expanded = IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: "o/r");
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(expanded.Blocks));
        var link = para.Segments.Single(s => s.Style.HasFlag(MarkdownStyle.Link));
        Assert.Equal("#471", link.Text);
        Assert.Equal("https://github.com/o/r/issues/471", link.Url);
    }

    [Fact]
    public void Expand_ShortForm_WithoutContext_NoChange()
    {
        var doc = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("fix in #471 done", MarkdownStyle.None)]),
        ]);

        var expanded = IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: null);
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(expanded.Blocks));
        Assert.Equal("fix in #471 done", Assert.Single(para.Segments).Text);
        Assert.Null(Assert.Single(para.Segments).Url);
    }

    [Fact]
    public void Expand_LinkSegment_Skipped()
    {
        // 已是链接段（Url 非空）不二次处理 — 防嵌套链接破坏既有 Url
        var doc = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("o/r#123", MarkdownStyle.Link, "https://existing.io/x")]),
        ]);

        var expanded = IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: null);
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(expanded.Blocks));
        var seg = Assert.Single(para.Segments);
        Assert.Equal("https://existing.io/x", seg.Url);
    }

    [Fact]
    public void Expand_StyledSegment_KeepsStyleOnContexts()
    {
        // 带样式段（如强调）拆分后前后文保留原样式，引用子串叠 Link
        var doc = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("bold o/r#9 tail", MarkdownStyle.Bold)]),
        ]);

        var expanded = IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: null);
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(expanded.Blocks));
        Assert.Equal(3, para.Segments.Count);
        Assert.Equal(MarkdownStyle.Bold, para.Segments[0].Style);
        Assert.Equal(MarkdownStyle.Bold | MarkdownStyle.Link, para.Segments[1].Style);
        Assert.Equal(MarkdownStyle.Bold, para.Segments[2].Style);
    }

    [Fact]
    public void Expand_HeadingReference_Linked()
    {
        var doc = new MarkdownDocument([
            new MarkdownHeading(1, [new MarkdownSegment("release o/r#10", MarkdownStyle.Bold)]),
        ]);

        var expanded = IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: null);
        var heading = Assert.IsType<MarkdownHeading>(Assert.Single(expanded.Blocks));
        Assert.Contains(heading.Segments, s => s.Url == "https://github.com/o/r/issues/10");
    }

    [Fact]
    public void Expand_NoReferences_ReturnsSameDocument()
    {
        var doc = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("普通文本无引用", MarkdownStyle.None)]),
        ]);

        Assert.Same(doc, IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: "o/r"));
    }

    [Fact]
    public void Expand_MultipleReferences_InOneSegment()
    {
        var doc = new MarkdownDocument([
            new MarkdownParagraph([new MarkdownSegment("a o/r#1 b o/r#2 c", MarkdownStyle.None)]),
        ]);

        var expanded = IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: null);
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(expanded.Blocks));
        Assert.Equal(5, para.Segments.Count);
        Assert.Equal(2, para.Segments.Count(s => s.Url is not null));
    }

    [Fact]
    public void Expand_CodeBlock_Unchanged()
    {
        var doc = new MarkdownDocument([
            new MarkdownCode("text", "var s = \"o/r#123\";"),
        ]);

        Assert.Same(doc, IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: null));
    }

    [Fact]
    public void Expand_QuoteParagraphReference_Linked()
    {
        var doc = new MarkdownDocument([
            new MarkdownQuote([
                new MarkdownParagraph([new MarkdownSegment("quoted o/r#5", MarkdownStyle.None)]),
            ]),
        ]);

        var expanded = IssueLinkExpander.ExpandDocument(doc, defaultOwnerRepo: null);
        var quote = Assert.IsType<MarkdownQuote>(Assert.Single(expanded.Blocks));
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(quote.Children));
        Assert.Contains(para.Segments, s => s.Url == "https://github.com/o/r/issues/5");
    }

    /// <summary>git remote URL → owner/repo 解析（ssh/https 两种形态 + .git 后缀容忍）。</summary>
    [Theory]
    [InlineData("git@github.com:owner/repo.git", "owner/repo")]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("https://gitlab.com/owner/repo.git", null)]
    [InlineData("https://github.com/owner", null)]
    [InlineData("not-a-url", null)]
    public void GitRemote_ParseGitHub(string remoteUrl, string? expected) =>
        Assert.Equal(expected, GitRemoteResolver.ParseGitHub(remoteUrl));
}
