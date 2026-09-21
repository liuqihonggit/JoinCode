namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneZoomToolHandlers 单元测试 — AC-03 链路推荐 zoom → zoom/detect
/// </summary>
public sealed class DesktopSceneZoomToolHandlersTests {
    /// <summary>AC-03: 看不清时 suggested_next 含 desktop_zoom</summary>
    [Fact]
    public async Task Zoom_NotClearEnough_SuggestsZoom() {
        var zoomMock = new Mock<IDesktopSceneZoomService>();
        zoomMock.Setup(z => z.ZoomAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneZoom("fake", "L0.2", 1, 960, 540, false));
        var handler = new DesktopSceneZoomToolHandlers(zoomMock.Object);

        var result = await handler.ZoomAsync("sc_test", 2);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("desktop_zoom");
        text.Should().Contain("继续");
    }

    /// <summary>AC-03: 看清时 suggested_next 含 desktop_detect</summary>
    [Fact]
    public async Task Zoom_ClearEnough_SuggestsDetect() {
        var zoomMock = new Mock<IDesktopSceneZoomService>();
        zoomMock.Setup(z => z.ZoomAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneZoom("fake", "L0.2.1.3", 4, 60, 34, true));
        var handler = new DesktopSceneZoomToolHandlers(zoomMock.Object);

        var result = await handler.ZoomAsync("sc_test", 3);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("desktop_detect");
        text.Should().Contain("识别");
    }
}