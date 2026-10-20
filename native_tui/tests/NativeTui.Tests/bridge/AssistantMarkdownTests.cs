namespace NativeTui.Tests.Bridge;

/// <summary>
/// assistant 消息 Markdown 分流测试（M7-4）— 事件层扩展与消费层分流：
/// AssistantMessage.Markdown 非空 → MarkdownMessageItem（块模型渲染）；null → AssistantMessageItem（纯文本，旧 JSON 兼容）；
/// BridgeProtocol JSON 往返保多态块结构（paragraph/heading/code/list 等 type discriminator），
/// null 不输出字段（WhenWritingNull — 引擎侧旧协议逐字节一致）。
/// </summary>
public sealed class AssistantMarkdownTests
{
    private readonly ReplView _view = new();
    private readonly SpinnerItem _spinner = new();
    private readonly BannerItem _banner = new("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder");
    private readonly ReplBridgeDriver _driver;

    public AssistantMarkdownTests()
    {
        _driver = new ReplBridgeDriver(_view, _spinner, _banner);
        _view.Messages.Add(_banner);
        _view.Messages.Add(_spinner);
    }

    /// <summary>构造演示文档（heading + paragraph 行内样式 + code）。</summary>
    private static MarkdownDocument DemoDocument() => new(
    [
        new MarkdownHeading(2, [new MarkdownSegment("标题", MarkdownStyle.None)]),
        new MarkdownParagraph(
        [
            new MarkdownSegment("plain ", MarkdownStyle.None),
            new MarkdownSegment("bold", MarkdownStyle.Bold),
            new MarkdownSegment(" ", MarkdownStyle.None),
            new MarkdownSegment("code", MarkdownStyle.Code),
        ]),
        new MarkdownCode("cs", "var a = 1;"),
    ]);

    [Fact]
    public void Apply_AssistantMessageWithMarkdownRendersMarkdownItem()
    {
        _driver.Apply(new BridgeEvent.AssistantMessage("# 演示", DemoDocument()));

        var item = Assert.IsType<MarkdownMessageItem>(_view.Messages.Items[^2]);
        Assert.Equal(3, item.Document.Blocks.Count);
    }

    [Fact]
    public void Apply_AssistantMessageWithoutMarkdownKeepsPlainItem()
    {
        _driver.Apply(new BridgeEvent.AssistantMessage("纯文本回复"));

        Assert.IsType<AssistantMessageItem>(_view.Messages.Items[^2]);
    }

    [Fact]
    public void Serialize_AssistantMessageMarkdownRoundTrips()
    {
        var json = BridgeProtocol.SerializeEvent(new BridgeEvent.AssistantMessage("# 演示", DemoDocument()));

        var reader = new StringReader(json);
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadEvent(reader, out var @event, out _));
        var message = Assert.IsType<BridgeEvent.AssistantMessage>(@event);
        Assert.Equal("# 演示", message.Text);
        Assert.NotNull(message.Markdown);
        Assert.Equal(3, message.Markdown.Blocks.Count);

        // 多态块还原：heading/paragraph/code
        Assert.IsType<MarkdownHeading>(message.Markdown.Blocks[0]);
        Assert.IsType<MarkdownParagraph>(message.Markdown.Blocks[1]);
        Assert.IsType<MarkdownCode>(message.Markdown.Blocks[2]);
    }

    [Fact]
    public void Serialize_AssistantMessageWithoutMarkdownOmitsField()
    {
        var json = BridgeProtocol.SerializeEvent(new BridgeEvent.AssistantMessage("纯文本"));

        // WhenWritingNull：markdown 字段不输出（引擎侧旧协议逐字节一致）
        Assert.DoesNotContain("markdown", json);
    }

    [Fact]
    public void ReadEvent_AssistantMessageWithMarkdownBlocksRestoresStructure()
    {
        var json = """{"type":"assistant_message","text":"# t","markdown":{"blocks":[{"type":"heading","level":1,"segments":[{"text":"t","style":0}]},{"type":"paragraph","segments":[{"text":"a","style":0},{"text":"b","style":1}]},{"type":"code","language":"cs","text":"var a = 1;"},{"type":"list","ordered":true,"start":3,"items":[{"children":[{"type":"paragraph","segments":[{"text":"x","style":0}]}]}]},{"type":"rule"},{"type":"quote","children":[{"type":"paragraph","segments":[{"text":"q","style":0}]}]}]}}""" + "\n";
        var reader = new StringReader(json);
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadEvent(reader, out var @event, out _));

        var message = Assert.IsType<BridgeEvent.AssistantMessage>(@event);
        var blocks = message.Markdown!.Blocks;
        Assert.Equal(6, blocks.Count);
        Assert.Equal(1, Assert.IsType<MarkdownHeading>(blocks[0]).Level);
        Assert.Equal(MarkdownStyle.Bold, Assert.IsType<MarkdownParagraph>(blocks[1]).Segments[1].Style);
        var code = Assert.IsType<MarkdownCode>(blocks[2]);
        Assert.Equal(("cs", "var a = 1;"), (code.Language, code.Text));
        var list = Assert.IsType<MarkdownList>(blocks[3]);
        Assert.True(list.Ordered);
        Assert.Equal(3, list.Start);
        Assert.IsType<MarkdownRule>(blocks[4]);
        Assert.IsType<MarkdownQuote>(blocks[5]);
    }
}
