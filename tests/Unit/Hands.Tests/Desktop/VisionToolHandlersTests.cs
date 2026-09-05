namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// VisionToolHandlers 单元测试 — 验证 detect_ui_elements / find_element 工具逻辑
/// </summary>
public sealed class VisionToolHandlersTests
{
    private static Mock<IUiElementDetector> CreateDetectorMock()
    {
        var mock = new Mock<IUiElementDetector>();
        mock
            .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UiElementDetectionResult(
                [
                    new(UiElementType.Button, "确定", "蓝色确认按钮", 100, 200, 80, 30, ElementState.Normal, 0.95),
                    new(UiElementType.TextBox, null, "用户名输入框", 50, 100, 200, 25, ElementState.Focused, 0.88)
                ],
                1920, 1080));
        return mock;
    }

    private static Mock<IScreenCaptureService> CreateCaptureMock(string base64 = "fakeBase64Png")
    {
        var mock = new Mock<IScreenCaptureService>();
        mock
            .Setup(c => c.CaptureFullScreenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(base64);
        return mock;
    }

    [Fact]
    public async Task DetectUiElements_WithProvidedScreenshot_ReturnsFormattedElements()
    {
        var detectorMock = CreateDetectorMock();
        var captureMock = CreateCaptureMock();
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.DetectUiElementsAsync("providedBase64");

        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCountGreaterThanOrEqualTo(1);
        var text = result.Content[0].Text!;
        text.Should().Contain("1920x1080");
        text.Should().Contain("2 个 UI 元素");
        text.Should().Contain("Button");
        text.Should().Contain("确定");
        text.Should().Contain("(100,200)");

        captureMock.Verify(c => c.CaptureFullScreenAsync(It.IsAny<CancellationToken>()), Times.Never);
        detectorMock.Verify(d => d.DetectAsync("providedBase64", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DetectUiElements_WithoutScreenshot_CapturesScreenFirst()
    {
        var detectorMock = CreateDetectorMock();
        var captureMock = CreateCaptureMock("autoCapturedBase64");
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.DetectUiElementsAsync(null);

        result.IsError.Should().BeFalse();
        captureMock.Verify(c => c.CaptureFullScreenAsync(It.IsAny<CancellationToken>()), Times.Once);
        detectorMock.Verify(d => d.DetectAsync("autoCapturedBase64", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DetectUiElements_CaptureFails_ReturnsError()
    {
        var detectorMock = new Mock<IUiElementDetector>();
        var captureMock = new Mock<IScreenCaptureService>();
        captureMock
            .Setup(c => c.CaptureFullScreenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.DetectUiElementsAsync(null);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("截图失败");
        detectorMock.Verify(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindElement_Found_ReturnsCoordinates()
    {
        var detectorMock = new Mock<IUiElementDetector>();
        detectorMock
            .Setup(d => d.FindByDescriptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UiElement(UiElementType.Button, "保存", "保存按钮", 300, 400, 80, 30, ElementState.Normal, 0.92));
        var captureMock = CreateCaptureMock();
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.FindElementAsync("保存按钮", "providedBase64");

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("Button");
        text.Should().Contain("保存");
        text.Should().Contain("(300,400)");
        text.Should().Contain("mouse_click");
        text.Should().Contain("x=340");
        text.Should().Contain("y=415");
    }

    [Fact]
    public async Task FindElement_NotFound_ReturnsNotFoundMessage()
    {
        var detectorMock = new Mock<IUiElementDetector>();
        detectorMock
            .Setup(d => d.FindByDescriptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UiElement?)null);
        var captureMock = CreateCaptureMock();
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.FindElementAsync("不存在的元素", "providedBase64");

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("未找到");
        result.Content[0].Text.Should().Contain("不存在的元素");
    }

    [Fact]
    public async Task FindElement_WithoutScreenshot_CapturesScreenFirst()
    {
        var detectorMock = new Mock<IUiElementDetector>();
        detectorMock
            .Setup(d => d.FindByDescriptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UiElement?)null);
        var captureMock = CreateCaptureMock("autoCapture");
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        await handler.FindElementAsync("按钮", null);

        captureMock.Verify(c => c.CaptureFullScreenAsync(It.IsAny<CancellationToken>()), Times.Once);
        detectorMock.Verify(d => d.FindByDescriptionAsync("autoCapture", "按钮", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindElement_CaptureFails_ReturnsError()
    {
        var detectorMock = new Mock<IUiElementDetector>();
        var captureMock = new Mock<IScreenCaptureService>();
        captureMock
            .Setup(c => c.CaptureFullScreenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.FindElementAsync("按钮", null);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("截图失败");
    }

    /// <summary>LLM 调用超时（OperationCanceledException）时应返回友好错误，而非异常传播卡死</summary>
    [Fact]
    public async Task DetectUiElements_LlmTimeout_ReturnsFriendlyError()
    {
        var detectorMock = new Mock<IUiElementDetector>();
        detectorMock
            .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, ct) => Task.FromException<UiElementDetectionResult>(new OperationCanceledException(ct)));
        var captureMock = CreateCaptureMock();
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.DetectUiElementsAsync("base64");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("超时");
    }

    /// <summary>LLM 调用超时时 find_element 应返回友好错误，而非异常传播卡死</summary>
    [Fact]
    public async Task FindElement_LlmTimeout_ReturnsFriendlyError()
    {
        var detectorMock = new Mock<IUiElementDetector>();
        detectorMock
            .Setup(d => d.FindByDescriptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((_, _, ct) => Task.FromException<UiElement?>(new OperationCanceledException(ct)));
        var captureMock = CreateCaptureMock();
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.FindElementAsync("按钮", "base64");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("超时");
    }

    /// <summary>LLM 返回空结果（0x0 尺寸）时应返回友好错误提示检查 API Key，而非误导性的空成功</summary>
    [Fact]
    public async Task DetectUiElements_LlmReturnsEmpty_ReturnsFriendlyError()
    {
        var detectorMock = new Mock<IUiElementDetector>();
        detectorMock
            .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UiElementDetectionResult([], 0, 0));
        var captureMock = CreateCaptureMock();
        var handler = new VisionToolHandlers(detectorMock.Object, captureMock.Object);

        var result = await handler.DetectUiElementsAsync("base64");

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("API Key");
    }
}
