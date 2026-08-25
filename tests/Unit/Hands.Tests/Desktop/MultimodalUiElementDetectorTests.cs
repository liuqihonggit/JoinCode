namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// MultimodalUiElementDetector 纯方法单元测试 — 验证 JSON 解析、枚举映射、LLM 交互
/// </summary>
public sealed class MultimodalUiElementDetectorTests
{
    #region ParseElementType

    [Theory]
    [InlineData("button", UiElementType.Button)]
    [InlineData("Button", UiElementType.Button)]
    [InlineData("btn", UiElementType.Button)]
    [InlineData("textbox", UiElementType.TextBox)]
    [InlineData("input", UiElementType.TextBox)]
    [InlineData("menu", UiElementType.Menu)]
    [InlineData("menuitem", UiElementType.MenuItem)]
    [InlineData("dialog", UiElementType.Dialog)]
    [InlineData("progressbar", UiElementType.ProgressBar)]
    [InlineData("checkbox", UiElementType.CheckBox)]
    [InlineData("radiobutton", UiElementType.RadioButton)]
    [InlineData("icon", UiElementType.Icon)]
    [InlineData("text", UiElementType.Text)]
    [InlineData("label", UiElementType.Text)]
    [InlineData("image", UiElementType.Image)]
    [InlineData("link", UiElementType.Link)]
    [InlineData("combobox", UiElementType.ComboBox)]
    [InlineData("dropdown", UiElementType.ComboBox)]
    [InlineData("listitem", UiElementType.ListItem)]
    [InlineData("titlebar", UiElementType.TitleBar)]
    [InlineData("scrollbar", UiElementType.ScrollBar)]
    public void ParseElementType_KnownTypes_ReturnCorrectEnum(string input, UiElementType expected)
    {
        MultimodalUiElementDetector.ParseElementType(input).Should().Be(expected);
    }

    [Fact]
    public void ParseElementType_UnknownType_ReturnsUnknown()
    {
        MultimodalUiElementDetector.ParseElementType("foobar").Should().Be(UiElementType.Unknown);
        MultimodalUiElementDetector.ParseElementType(null).Should().Be(UiElementType.Unknown);
        MultimodalUiElementDetector.ParseElementType(string.Empty).Should().Be(UiElementType.Unknown);
    }

    #endregion

    #region ParseElementState

    [Theory]
    [InlineData("normal", ElementState.Normal)]
    [InlineData("disabled", ElementState.Disabled)]
    [InlineData("selected", ElementState.Selected)]
    [InlineData("hovered", ElementState.Hovered)]
    [InlineData("focused", ElementState.Focused)]
    [InlineData("hidden", ElementState.Hidden)]
    [InlineData("pressed", ElementState.Pressed)]
    [InlineData("enabled", ElementState.Normal)]
    [InlineData("checked", ElementState.Selected)]
    [InlineData("active", ElementState.Focused)]
    public void ParseElementState_KnownStates_ReturnCorrectEnum(string input, ElementState expected)
    {
        MultimodalUiElementDetector.ParseElementState(input).Should().Be(expected);
    }

    [Fact]
    public void ParseElementState_UnknownState_DefaultsToNormal()
    {
        MultimodalUiElementDetector.ParseElementState("foobar").Should().Be(ElementState.Normal);
        MultimodalUiElementDetector.ParseElementState(null).Should().Be(ElementState.Normal);
    }

    #endregion

    #region ExtractJson

    [Fact]
    public void ExtractJson_PureJson_ReturnsAsIs()
    {
        var json = """{"found": true}""";
        MultimodalUiElementDetector.ExtractJson(json).Should().Be(json);
    }

    [Fact]
    public void ExtractJson_MarkdownCodeBlock_ExtractsInnerJson()
    {
        var response = """
            ```json
            {"found": true, "element": null}
            ```
            """;
        var result = MultimodalUiElementDetector.ExtractJson(response);
        result.Should().Contain("""{"found": true, "element": null}""");
    }

    [Fact]
    public void ExtractJson_JsonWithSurroundingText_ExtractsJsonObject()
    {
        var response = "Here is the result: {\"found\": true} done.";
        var result = MultimodalUiElementDetector.ExtractJson(response);
        result.Should().Be("""{"found": true}""");
    }

    [Fact]
    public void ExtractJson_EmptyInput_ReturnsEmpty()
    {
        MultimodalUiElementDetector.ExtractJson("").Should().BeEmpty();
        MultimodalUiElementDetector.ExtractJson("   ").Should().BeEmpty();
    }

    #endregion

    #region ParseDetectionResult

    [Fact]
    public void ParseDetectionResult_ValidJson_ReturnsElements()
    {
        var json = """
            {
              "imageWidth": 1920,
              "imageHeight": 1080,
              "elements": [
                {
                  "type": "button",
                  "text": "确定",
                  "description": "蓝色确认按钮",
                  "x": 100,
                  "y": 200,
                  "width": 80,
                  "height": 30,
                  "state": "normal",
                  "confidence": 0.95
                },
                {
                  "type": "textbox",
                  "text": null,
                  "description": "用户名输入框",
                  "x": 50,
                  "y": 100,
                  "width": 200,
                  "height": 25,
                  "state": "focused",
                  "confidence": 0.88
                }
              ]
            }
            """;

        var result = MultimodalUiElementDetector.ParseDetectionResult(json);

        result.ImageWidth.Should().Be(1920);
        result.ImageHeight.Should().Be(1080);
        result.Elements.Should().HaveCount(2);

        result.Elements[0].Type.Should().Be(UiElementType.Button);
        result.Elements[0].Text.Should().Be("确定");
        result.Elements[0].X.Should().Be(100);
        result.Elements[0].Confidence.Should().Be(0.95);

        result.Elements[1].Type.Should().Be(UiElementType.TextBox);
        result.Elements[1].State.Should().Be(ElementState.Focused);
    }

    [Fact]
    public void ParseDetectionResult_MarkdownWrapped_ParsesCorrectly()
    {
        var response = """
            ```json
            {"imageWidth": 800, "imageHeight": 600, "elements": [{"type": "link", "text": "点击这里", "description": "超链接", "x": 10, "y": 20, "width": 60, "height": 20, "state": "normal", "confidence": 0.9}]}
            ```
            """;

        var result = MultimodalUiElementDetector.ParseDetectionResult(response);

        result.ImageWidth.Should().Be(800);
        result.Elements.Should().HaveCount(1);
        result.Elements[0].Type.Should().Be(UiElementType.Link);
    }

    [Fact]
    public void ParseDetectionResult_EmptyElements_ReturnsEmptyList()
    {
        var json = """{"imageWidth": 100, "imageHeight": 100, "elements": []}""";
        var result = MultimodalUiElementDetector.ParseDetectionResult(json);
        result.Elements.Should().BeEmpty();
        result.ImageWidth.Should().Be(100);
    }

    [Fact]
    public void ParseDetectionResult_InvalidJson_ReturnsEmptyResult()
    {
        var result = MultimodalUiElementDetector.ParseDetectionResult("not json at all");
        result.Elements.Should().BeEmpty();
        result.ImageWidth.Should().Be(0);
    }

    [Fact]
    public void ParseDetectionResult_EmptyInput_ReturnsEmptyResult()
    {
        var result = MultimodalUiElementDetector.ParseDetectionResult("");
        result.Elements.Should().BeEmpty();
    }

    [Fact]
    public void ParseDetectionResult_MissingImageDimensions_DefaultsToZero()
    {
        var json = """{"elements": []}""";
        var result = MultimodalUiElementDetector.ParseDetectionResult(json);
        result.ImageWidth.Should().Be(0);
        result.ImageHeight.Should().Be(0);
    }

    #endregion

    #region ParseFindResult

    [Fact]
    public void ParseFindResult_FoundTrue_ReturnsElement()
    {
        var json = """
            {
              "found": true,
              "element": {
                "type": "button",
                "text": "取消",
                "description": "红色取消按钮",
                "x": 300,
                "y": 400,
                "width": 80,
                "height": 30,
                "state": "normal",
                "confidence": 0.92
              }
            }
            """;

        var result = MultimodalUiElementDetector.ParseFindResult(json);

        result.Should().NotBeNull();
        result!.Type.Should().Be(UiElementType.Button);
        result.Text.Should().Be("取消");
        result.X.Should().Be(300);
        result.Confidence.Should().Be(0.92);
    }

    [Fact]
    public void ParseFindResult_FoundFalse_ReturnsNull()
    {
        var json = """{"found": false, "element": null}""";
        var result = MultimodalUiElementDetector.ParseFindResult(json);
        result.Should().BeNull();
    }

    [Fact]
    public void ParseFindResult_InvalidJson_ReturnsNull()
    {
        var result = MultimodalUiElementDetector.ParseFindResult("not json");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseFindResult_EmptyInput_ReturnsNull()
    {
        var result = MultimodalUiElementDetector.ParseFindResult("");
        result.Should().BeNull();
    }

    #endregion

    #region DetectAsync (with Mock IQueryService)

    [Fact]
    public async Task DetectAsync_ValidResponse_ReturnsParsedElements()
    {
        var llmResponse = """
            {"imageWidth": 1920, "imageHeight": 1080, "elements": [{"type": "button", "text": "OK", "description": "确认按钮", "x": 1, "y": 2, "width": 3, "height": 4, "state": "normal", "confidence": 0.9}]}
            """;

        var mockQueryService = new Mock<IQueryService>();
        mockQueryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, llmResponse) });

        var detector = new MultimodalUiElementDetector(mockQueryService.Object);

        var result = await detector.DetectAsync("base64dummy");

        result.Elements.Should().HaveCount(1);
        result.Elements[0].Text.Should().Be("OK");
        result.ImageWidth.Should().Be(1920);
        mockQueryService.Verify(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DetectAsync_EmptyLlmResponse_ReturnsEmptyResult()
    {
        var mockQueryService = new Mock<IQueryService>();
        mockQueryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, "") });

        var detector = new MultimodalUiElementDetector(mockQueryService.Object);

        var result = await detector.DetectAsync("base64dummy");

        result.Elements.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_NullOrWhiteSpaceBase64_ThrowsArgumentException()
    {
        var mockQueryService = new Mock<IQueryService>();
        var detector = new MultimodalUiElementDetector(mockQueryService.Object);

        var act = async () => await detector.DetectAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DetectAsync_PassesMultimodalContentBlock_ToQueryService()
    {
        MessageList? capturedMessages = null;
        var mockQueryService = new Mock<IQueryService>();
        mockQueryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions?, IChatClient?, CancellationToken>((msgs, _, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, """{"imageWidth":0,"imageHeight":0,"elements":[]}""") });

        var detector = new MultimodalUiElementDetector(mockQueryService.Object);
        await detector.DetectAsync("myBase64Image");

        capturedMessages.Should().NotBeNull();
        capturedMessages!.Count.Should().Be(2);
        capturedMessages[0].Role.Should().Be(MessageRole.System);

        var userMsg = capturedMessages[1];
        userMsg.Role.Should().Be(MessageRole.User);
        userMsg.ContentBlocks.Should().NotBeNull();
        userMsg.ContentBlocks!.Should().HaveCount(1);
        userMsg.ContentBlocks[0].Type.Should().Be(ToolContentType.Image);
        userMsg.ContentBlocks[0].Data.Should().Be("myBase64Image");
        userMsg.ContentBlocks[0].MimeType.Should().Be("image/png");
    }

    #endregion

    #region FindByDescriptionAsync (with Mock IQueryService)

    [Fact]
    public async Task FindByDescriptionAsync_FoundTrue_ReturnsElement()
    {
        var llmResponse = """
            {"found": true, "element": {"type": "button", "text": "保存", "description": "保存按钮", "x": 10, "y": 20, "width": 80, "height": 30, "state": "normal", "confidence": 0.95}}
            """;

        var mockQueryService = new Mock<IQueryService>();
        mockQueryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, llmResponse) });

        var detector = new MultimodalUiElementDetector(mockQueryService.Object);

        var result = await detector.FindByDescriptionAsync("base64dummy", "保存按钮");

        result.Should().NotBeNull();
        result!.Text.Should().Be("保存");
        result.Type.Should().Be(UiElementType.Button);
    }

    [Fact]
    public async Task FindByDescriptionAsync_FoundFalse_ReturnsNull()
    {
        var llmResponse = """{"found": false, "element": null}""";

        var mockQueryService = new Mock<IQueryService>();
        mockQueryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, llmResponse) });

        var detector = new MultimodalUiElementDetector(mockQueryService.Object);

        var result = await detector.FindByDescriptionAsync("base64dummy", "不存在的元素");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByDescriptionAsync_NullOrWhiteSpaceArguments_ThrowsArgumentException()
    {
        var mockQueryService = new Mock<IQueryService>();
        var detector = new MultimodalUiElementDetector(mockQueryService.Object);

        var act1 = async () => await detector.FindByDescriptionAsync("", "desc");
        await act1.Should().ThrowAsync<ArgumentException>();

        var act2 = async () => await detector.FindByDescriptionAsync("img", "");
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FindByDescriptionAsync_PassesDescriptionInUserMessage()
    {
        MessageList? capturedMessages = null;
        var mockQueryService = new Mock<IQueryService>();
        mockQueryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions?, IChatClient?, CancellationToken>((msgs, _, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, """{"found":false,"element":null}""") });

        var detector = new MultimodalUiElementDetector(mockQueryService.Object);
        await detector.FindByDescriptionAsync("img", "红色的停止按钮");

        capturedMessages.Should().NotBeNull();
        var userMsg = capturedMessages![1];
        userMsg.Role.Should().Be(MessageRole.User);
        userMsg.Content.Should().Contain("红色的停止按钮");
    }

    #endregion
}
