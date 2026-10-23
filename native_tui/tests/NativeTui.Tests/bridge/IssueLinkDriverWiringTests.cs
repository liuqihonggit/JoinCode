namespace NativeTui.Tests.Bridge;

/// <summary>
/// DSG043 尾批 — issue 引用链接化 Driver 接线测试：AssistantMessage 的 markdown 经
/// IssueLinkExpander.ExpandDocument 展开（issueOwnerRepo 上下文注入），构造点 Driver:169。
/// </summary>
public sealed class IssueLinkDriverWiringTests
{
    /// <summary>完整形态 owner/repo#N 恒展开（无上下文也展开）。</summary>
    [Fact]
    public void AssistantMessage_MarkdownIssueReference_ExpandedToLinkSegment()
    {
        var (driver, view) = TestDriver(issueOwnerRepo: null);
        driver.BeginTurn();
        driver.Apply(new BridgeEvent.TokenDelta(1, "s"));
        driver.Apply(new BridgeEvent.AssistantMessage(
            Text: "see o/r#7",
            Markdown: new MarkdownDocument([new MarkdownParagraph([new MarkdownSegment("see o/r#7", MarkdownStyle.None)])])));

        var item = Assert.IsType<MarkdownMessageItem>(view.Messages.Items.First(i => i is MarkdownMessageItem));
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(item.Document.Blocks));
        var link = para.Segments.Single(s => s.Url is not null);
        Assert.Equal("o/r#7", link.Text);
        Assert.Equal("https://github.com/o/r/issues/7", link.Url);
        Assert.True(link.Style.HasFlag(MarkdownStyle.Link));
    }

    /// <summary>短形态 #N 需上下文 — issueOwnerRepo 注入时展开为同库 issue。</summary>
    [Fact]
    public void AssistantMessage_ShortForm_WithContext_Expanded()
    {
        var (driver, view) = TestDriver(issueOwnerRepo: "my/repo");
        driver.BeginTurn();
        driver.Apply(new BridgeEvent.TokenDelta(1, "s"));
        driver.Apply(new BridgeEvent.AssistantMessage(
            Text: "fix #9",
            Markdown: new MarkdownDocument([new MarkdownParagraph([new MarkdownSegment("fix #9", MarkdownStyle.None)])])));

        var item = Assert.IsType<MarkdownMessageItem>(view.Messages.Items.First(i => i is MarkdownMessageItem));
        var para = Assert.IsType<MarkdownParagraph>(Assert.Single(item.Document.Blocks));
        var link = para.Segments.Single(s => s.Url is not null);
        Assert.Equal("https://github.com/my/repo/issues/9", link.Url);
    }

    /// <summary>纯文本消息（markdown null）不展开 — AssistantMessageItem 路径零影响。</summary>
    [Fact]
    public void AssistantMessage_PlainText_NoExpansion()
    {
        var (driver, view) = TestDriver(issueOwnerRepo: "my/repo");
        driver.BeginTurn();
        driver.Apply(new BridgeEvent.TokenDelta(1, "s"));
        driver.Apply(new BridgeEvent.AssistantMessage(Text: "fix #9", Markdown: null));

        Assert.IsType<AssistantMessageItem>(view.Messages.Items.First(i => i is AssistantMessageItem));
    }

    /// <summary>构造测试环境（FakeView + Driver — issueOwnerRepo 注入面）。</summary>
    private static (ReplBridgeDriver Driver, ReplView View) TestDriver(string? issueOwnerRepo)
    {
        var view = new ReplView();
        var spinner = new SpinnerItem { Message = string.Empty };
        view.Messages.Add(spinner);
        var banner = new BannerItem("v", "m", "b", "/tmp");
        var driver = new ReplBridgeDriver(view, spinner, banner, onPermissionRequest: null,
            codeHighlighter: null, tokenUsageTracker: null, sizeSource: null, streamingProjector: null,
            issueOwnerRepo: issueOwnerRepo);
        return (driver, view);
    }
}
