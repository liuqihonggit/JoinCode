namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneZoomToolHandlers 单元测试 — AC-03 链路推荐 zoom → zoom/detect。
/// <para>断言走 JsonAssert 语义路径（不依赖紧凑排版与非 ASCII 转义）。</para>
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
        var text = TextOf(result);
        JsonAssert.AssertArrayContains(text, "suggested_next",
            e => e.GetProperty("tool").GetString() == "desktop_zoom");
        JsonAssert.AssertAnyStringContains(text, "suggested_next", "reason", "继续");
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
        var text = TextOf(result);
        JsonAssert.AssertArrayContains(text, "suggested_next",
            e => e.GetProperty("tool").GetString() == "desktop_detect");
        JsonAssert.AssertAnyStringContains(text, "suggested_next", "reason", "识别");
    }

    /// <summary>提取工具结果首个文本 — 缺失即抛异常（替代 null-forgiving）。</summary>
    private static string TextOf(ToolResult result) =>
        result.GetFirstText() ?? throw new InvalidOperationException("工具结果缺少文本内容");
}
