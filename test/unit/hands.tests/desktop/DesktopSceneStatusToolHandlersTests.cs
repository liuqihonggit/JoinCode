namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneStatusToolHandlers 单元测试 — AC-05 状态持久化（handler 逻辑）
/// </summary>
public sealed class DesktopSceneStatusToolHandlersTests {
    /// <summary>AC-05: 调 status 返回当前夹逼状态（层/格子/历史）</summary>
    [Fact]
    public async Task Status_ReturnsCurrentState_WithDepthAndCellAndHistory() {
        var stateStoreMock = new Mock<IDesktopSceneStateStore>();
        stateStoreMock.Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneState(
                "sc_001", 1, "L0.2",
                new[] { new ZoomHistoryEntry(1, "L0.2", 2, DateTimeOffset.UtcNow) },
                null, "zoom", DateTimeOffset.UtcNow));
        var handler = new DesktopSceneStatusToolHandlers(stateStoreMock.Object);

        var result = await handler.StatusAsync("sc_001");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("sc_001");
        text.Should().Contain("L0.2");
        text.Should().Contain("zoom_history");
        text.Should().Contain("current_depth");
    }

    /// <summary>AC-05: 场景不存在时返回明确提示</summary>
    [Fact]
    public async Task Status_SceneNotFound_ReturnsHint() {
        var stateStoreMock = new Mock<IDesktopSceneStateStore>();
        stateStoreMock.Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DesktopSceneState?)null);
        var handler = new DesktopSceneStatusToolHandlers(stateStoreMock.Object);

        var result = await handler.StatusAsync("sc_nonexistent");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("不存在");
    }
}