namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneLookToolHandlers 单元测试 — AC-02 链路推荐 look → zoom
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
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        root.TryGetProperty("scene_id", out _).Should().BeTrue("返回应含 scene_id");
        root.GetProperty("suggested_next").EnumerateArray().Should().Contain(
            e => e.GetProperty("tool").GetString() == "desktop_zoom");
        var reasons = root.GetProperty("suggested_next").EnumerateArray()
            .Select(e => e.GetProperty("reason").GetString() ?? string.Empty).ToList();
        reasons.Should().Contain(r => r.Contains("缩小", StringComparison.Ordinal));
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
        result.GetFirstText().Should().Contain("sc_test_001");
    }
}