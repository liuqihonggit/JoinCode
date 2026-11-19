namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneLookToolHandlers 单元测试 — AC-02 链路推荐 look → zoom。
/// <para>断言走 JsonAssert 语义路径（不依赖紧凑排版与非 ASCII 转义）。</para>
/// </summary>
public sealed class DesktopSceneLookToolHandlersTests {
    /// <summary>AC-02: 调 desktop_look 成功后返回 suggested_next 含 desktop_zoom</summary>
    [Fact]
    public async Task Look_ReturnsScreenshotAndSuggestsZoom() {
        var captureMock = new Mock<IDesktopSceneCaptureService>();
        captureMock.Setup(c => c.CaptureWithGridAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneCapture("fake_original", "fake_rendered", 1920, 1080, 2));
        var handler = new DesktopSceneLookToolHandlers(captureMock.Object);

        var result = await handler.LookAsync();

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText() ?? throw new InvalidOperationException("工具结果缺少文本");
        using var doc = JsonAssert.Parse(text);
        JsonAssert.Get(doc.RootElement, "scene_id");
        JsonAssert.AssertArrayContains(text, "suggested_next",
            e => e.GetProperty("tool").GetString() == "desktop_zoom");
        JsonAssert.AssertAnyStringContains(text, "suggested_next", "reason", "缩小");
    }

    /// <summary>AC-02: 传入 sceneId 时返回中携带该 sceneId</summary>
    [Fact]
    public async Task Look_WithSceneId_ReturnsSameSceneId() {
        var captureMock = new Mock<IDesktopSceneCaptureService>();
        captureMock.Setup(c => c.CaptureWithGridAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneCapture("fake", "fake", 100, 100, 2));
        var handler = new DesktopSceneLookToolHandlers(captureMock.Object);

        var result = await handler.LookAsync("sc_test_001");

        result.IsError.Should().BeFalse();
        JsonAssert.AssertString(result.GetFirstText() ?? throw new InvalidOperationException("工具结果缺少文本"),
            "scene_id", "sc_test_001");
    }
}
