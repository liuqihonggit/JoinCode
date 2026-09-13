namespace Vision.Tests.ToolHandlers;

/// <summary>
/// ImageMetaphorToolHandlers 单元测试 — 验证 M2 的 2 个 MCP 工具
/// </summary>
public sealed class ImageMetaphorToolHandlersTests
{
    private static Mock<IQueryService> CreateQueryServiceMock(string responseContent)
    {
        var mock = new Mock<IQueryService>();
        mock
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, responseContent) });
        return mock;
    }

    [Fact]
    public async Task ImageDescribe_ValidResponse_ShouldReturnFormattedText()
    {
        var llmResponse = """
            {"summary": "一个厨房场景", "labels": [{"label": "冰箱", "description": "白色双门冰箱", "suggested_attributes": ["品牌", "颜色"]}]}
            """;
        var mock = CreateQueryServiceMock(llmResponse);
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDescribeAsync("base64dummy");

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("厨房场景");
        text.Should().Contain("冰箱");
        text.Should().Contain("白色双门冰箱");
        text.Should().Contain("品牌");
        text.Should().Contain("颜色");
    }

    [Fact]
    public async Task ImageDescribe_EmptyBase64_ShouldReturnError()
    {
        var mock = new Mock<IQueryService>();
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDescribeAsync("");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS200]");
    }

    [Fact]
    public async Task ImageDescribe_EmptyResponse_ShouldReturnError()
    {
        var mock = CreateQueryServiceMock("");
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDescribeAsync("base64dummy");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS201]");
    }

    [Fact]
    public async Task ImageDescribe_UnparseableResponse_ShouldReturnRawText()
    {
        var mock = CreateQueryServiceMock("这不是JSON，只是普通文本响应");
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDescribeAsync("base64dummy");

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("普通文本响应");
    }

    [Fact]
    public async Task ImageDescribe_JsonCodeBlock_ShouldParseCorrectly()
    {
        var llmResponse = """
            ```json
            {"summary": "办公室", "labels": [{"label": "电脑", "description": "笔记本电脑", "suggested_attributes": ["型号", "状态"]}]}
            ```
            """;
        var mock = CreateQueryServiceMock(llmResponse);
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDescribeAsync("base64dummy");

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("办公室");
        text.Should().Contain("电脑");
    }

    [Fact]
    public async Task ImageDrillDown_ValidResponse_ShouldReturnFormattedText()
    {
        var llmResponse = """
            {"label": "冰箱", "attributes": [{"name": "品牌", "value": "海尔", "confidence": 0.9}], "suggested_next": ["冰箱门把手"], "has_more": true}
            """;
        var mock = CreateQueryServiceMock(llmResponse);
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDrillDownAsync("base64dummy", "冰箱", currentDepth: 1, maxDepth: 3);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("冰箱");
        text.Should().Contain("品牌");
        text.Should().Contain("海尔");
        text.Should().Contain("冰箱门把手");
        text.Should().Contain("还有更多属性可探索");
    }

    [Fact]
    public async Task ImageDrillDown_MaxDepthReached_ShouldStopExpansion()
    {
        var mock = new Mock<IQueryService>();
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDrillDownAsync("base64dummy", "冰箱", currentDepth: 4, maxDepth: 3);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("已达最大下钻深度");
        mock.Verify(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImageDrillDown_EmptyBase64_ShouldReturnError()
    {
        var mock = new Mock<IQueryService>();
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDrillDownAsync("", "冰箱");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS210]");
    }

    [Fact]
    public async Task ImageDrillDown_EmptyLabel_ShouldReturnError()
    {
        var mock = new Mock<IQueryService>();
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDrillDownAsync("base64dummy", "");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS211]");
    }

    [Fact]
    public async Task ImageDrillDown_EmptyResponse_ShouldReturnError()
    {
        var mock = CreateQueryServiceMock("");
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDrillDownAsync("base64dummy", "冰箱");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS212]");
    }

    [Fact]
    public async Task ImageDrillDown_HasMoreFalse_ShouldShowNoMoreMessage()
    {
        var llmResponse = """
            {"label": "桌子", "attributes": [{"name": "材质", "value": "木质", "confidence": 0.8}], "suggested_next": [], "has_more": false}
            """;
        var mock = CreateQueryServiceMock(llmResponse);
        var handlers = new ImageMetaphorToolHandlers(mock.Object);

        var result = await handlers.ImageDrillDownAsync("base64dummy", "桌子");

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("已无更多属性");
    }
}
