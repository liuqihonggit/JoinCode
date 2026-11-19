namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// AC-07 detect+click mock 完整链路测试 — 验证识别到按钮后推荐 click，点击后推荐 look。
/// <para>断言按 JSON 语义解析（DTO 序列化为紧凑格式、非 ASCII 转义），不锁死字符串排版。</para>
/// </summary>
public sealed class DesktopSceneDetectClickTests {
    /// <summary>AC-07: detect 识别到"="按钮后 suggested_next 含 desktop_click</summary>
    [Fact]
    public async Task Detect_FindsEqualsButton_SuggestsClick() {
        var detectMock = new Mock<IDesktopSceneDetectService>();
        detectMock.Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneDetection("sc_calc", [
                new DetectedUiElement("button", "=", "L0.2.1.3"),
                new DetectedUiElement("button", "1", "L0.2.1.0"),
                new DetectedUiElement("input", "显示区", "L0.0")
            ]));
        var handler = new DesktopSceneDetectToolHandlers(detectMock.Object);

        var result = await handler.DetectAsync("sc_calc");

        result.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(TextOf(result));
        var root = doc.RootElement;
        root.GetProperty("scene_id").GetString().Should().Be("sc_calc");
        root.GetProperty("elements").EnumerateArray().Should().Contain(
            e => e.GetProperty("type").GetString() == "button" && e.GetProperty("label").GetString() == "=");
        root.GetProperty("suggested_next").EnumerateArray().Should().Contain(
            e => e.GetProperty("tool").GetString() == "desktop_click");
        var reasons = root.GetProperty("suggested_next").EnumerateArray()
            .Select(e => e.GetProperty("reason").GetString() ?? string.Empty).ToList();
        reasons.Should().Contain(r => r.Contains("识别到按钮", StringComparison.Ordinal));
    }

    /// <summary>AC-07: detect 识别到输入框后 suggested_next 含 desktop_type</summary>
    [Fact]
    public async Task Detect_FindsInput_SuggestsType() {
        var detectMock = new Mock<IDesktopSceneDetectService>();
        detectMock.Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneDetection("sc_form", [
                new DetectedUiElement("input", "用户名", "L0.1")
            ]));
        var handler = new DesktopSceneDetectToolHandlers(detectMock.Object);

        var result = await handler.DetectAsync("sc_form");

        using var doc = JsonDocument.Parse(TextOf(result));
        var root = doc.RootElement;
        root.GetProperty("elements").EnumerateArray().Should().Contain(
            e => e.GetProperty("type").GetString() == "input" && e.GetProperty("label").GetString() == "用户名");
        root.GetProperty("suggested_next").EnumerateArray().Should().Contain(
            e => e.GetProperty("tool").GetString() == "desktop_type");
        var reasons = root.GetProperty("suggested_next").EnumerateArray()
            .Select(e => e.GetProperty("reason").GetString() ?? string.Empty).ToList();
        reasons.Should().Contain(r => r.Contains("输入框", StringComparison.Ordinal));
    }

    /// <summary>AC-07: click 成功后 suggested_next 含 desktop_look</summary>
    [Fact]
    public async Task Click_Succeeds_SuggestsLook() {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopOperation(DesktopOperationKind.Click, 500, 300, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null));
        var stateStoreMock = new Mock<IDesktopSceneStateStore>();
        stateStoreMock.Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneState("sc_calc", 3, "L0.2.1.3", [], "/fake/path", "detect", DateTimeOffset.UtcNow));
        var handler = new DesktopSceneClickToolHandlers(inputMock.Object, stateStoreMock.Object);

        var result = await handler.ClickAsync("sc_calc", 500, 300);

        result.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(TextOf(result));
        var root = doc.RootElement;
        root.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        root.GetProperty("x").GetInt32().Should().Be(500);
        root.GetProperty("y").GetInt32().Should().Be(300);
        root.GetProperty("suggested_next").EnumerateArray().Should().Contain(
            e => e.GetProperty("tool").GetString() == "desktop_look");
        var reasons = root.GetProperty("suggested_next").EnumerateArray()
            .Select(e => e.GetProperty("reason").GetString() ?? string.Empty).ToList();
        reasons.Should().Contain(r => r.Contains("点击完成", StringComparison.Ordinal));
        inputMock.Verify(i => i.ClickAsync(500, 300, MouseAction.Click, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>AC-07: click 失败（危险坐标拦截）后 suggested_next 含 desktop_look 重新确认</summary>
    [Fact]
    public async Task Click_BlockedBySafety_SuggestsLookToRetry() {
        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopOperation(DesktopOperationKind.Click, 0, 1080, null, MouseAction.Click, null, DateTimeOffset.UtcNow, false, "DangerousCoordinate"));
        var stateStoreMock = new Mock<IDesktopSceneStateStore>();
        stateStoreMock.Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DesktopSceneState?)null);
        var handler = new DesktopSceneClickToolHandlers(inputMock.Object, stateStoreMock.Object);

        var result = await handler.ClickAsync("sc_calc", 0, 1080);

        result.IsError.Should().BeFalse();
        using var doc = JsonDocument.Parse(TextOf(result));
        var root = doc.RootElement;
        root.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("DangerousCoordinate");
        root.GetProperty("suggested_next").EnumerateArray().Should().Contain(
            e => e.GetProperty("tool").GetString() == "desktop_look");
        var reasons = root.GetProperty("suggested_next").EnumerateArray()
            .Select(e => e.GetProperty("reason").GetString() ?? string.Empty).ToList();
        reasons.Should().Contain(r => r.Contains("点击失败", StringComparison.Ordinal));
    }

    /// <summary>AC-07: 完整链路 mock — look → zoom → detect → click 全链路 suggested_next 串联</summary>
    [Fact]
    public async Task FullChain_LookZoomDetectClick_SuggestedNextChain() {
        var captureMock = new Mock<IDesktopSceneCaptureService>();
        captureMock.Setup(c => c.CaptureWithGridAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneCapture("fake", "fake", 1920, 1080, 2));
        var lookHandler = new DesktopSceneLookToolHandlers(captureMock.Object);

        var zoomMock = new Mock<IDesktopSceneZoomService>();
        zoomMock.Setup(z => z.ZoomAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneZoom("fake", "L0.2.1.3", 4, 60, 34, true));
        var zoomHandler = new DesktopSceneZoomToolHandlers(zoomMock.Object);

        var detectMock = new Mock<IDesktopSceneDetectService>();
        detectMock.Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneDetection("sc_calc", [
                new DetectedUiElement("button", "=", "L0.2.1.3")
            ]));
        var detectHandler = new DesktopSceneDetectToolHandlers(detectMock.Object);

        var inputMock = new Mock<IDesktopInputService>();
        inputMock.Setup(i => i.ClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<MouseAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopOperation(DesktopOperationKind.Click, 500, 300, null, MouseAction.Click, null, DateTimeOffset.UtcNow, true, null));
        var stateStoreMock = new Mock<IDesktopSceneStateStore>();
        stateStoreMock.Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneState("sc_calc", 4, "L0.2.1.3", [], "/fake", "detect", DateTimeOffset.UtcNow));
        var clickHandler = new DesktopSceneClickToolHandlers(inputMock.Object, stateStoreMock.Object);

        var lookResult = await lookHandler.LookAsync("sc_calc");
        TextOf(lookResult).Should().Contain("desktop_zoom", "look 应推荐 zoom");

        var zoomResult = await zoomHandler.ZoomAsync("sc_calc", 2);
        TextOf(zoomResult).Should().Contain("desktop_detect", "zoom 看清后应推荐 detect");

        var detectResult = await detectHandler.DetectAsync("sc_calc");
        TextOf(detectResult).Should().Contain("desktop_click", "detect 识别到按钮应推荐 click");

        var clickResult = await clickHandler.ClickAsync("sc_calc", 500, 300);
        clickResult.IsError.Should().BeFalse();
        TextOf(clickResult).Should().Contain("desktop_look", "click 完成后应推荐 look 继续下一步");
    }

    /// <summary>提取工具结果首个文本 — 缺失即抛异常（替代 null-forgiving）。</summary>
    private static string TextOf(ToolResult result) =>
        result.GetFirstText() ?? throw new InvalidOperationException("工具结果缺少文本内容");
}
