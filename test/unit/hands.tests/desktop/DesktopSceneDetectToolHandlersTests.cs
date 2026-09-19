namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneDetectToolHandlers 单元测试 — AC-04 链路推荐 detect → click/type
/// </summary>
public sealed class DesktopSceneDetectToolHandlersTests {
    /// <summary>AC-04: 识别到 button 时 suggested_next 含 desktop_click</summary>
    [Fact]
    public async Task Detect_HasButton_SuggestsClick() {
        var detectMock = new Mock<IDesktopSceneDetectService>();
        detectMock.Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneDetection("sc_test", new[] { new DetectedUiElement("button", "确定", "L0.2.1") }));
        var handler = new DesktopSceneDetectToolHandlers(detectMock.Object);

        var result = await handler.DetectAsync("sc_test");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("desktop_click");
    }

    /// <summary>AC-04: 识别到 input 时 suggested_next 含 desktop_type</summary>
    [Fact]
    public async Task Detect_HasInput_SuggestsType() {
        var detectMock = new Mock<IDesktopSceneDetectService>();
        detectMock.Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneDetection("sc_test", new[] { new DetectedUiElement("input", "用户名", "L0.2.3") }));
        var handler = new DesktopSceneDetectToolHandlers(detectMock.Object);

        var result = await handler.DetectAsync("sc_test");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("desktop_type");
    }
}