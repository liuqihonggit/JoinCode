namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneMenuToolHandlers 单元测试 — AC-01 场景菜单可被发现
/// </summary>
public sealed class DesktopSceneMenuToolHandlersTests {
    /// <summary>AC-01: 调用 desktop_scene_menu 返回含 desktop_control 场景 + 工具集 + 建议流程</summary>
    [Fact]
    public async Task SceneMenu_ReturnsDesktopControlScene_WithToolsAndFlow() {
        var handler = new DesktopSceneMenuToolHandlers();
        var result = await handler.SceneMenuAsync();

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().NotBeNull();
        text.Should().Contain("desktop_control");
        text.Should().Contain("desktop_look");
        text.Should().Contain("desktop_zoom");
        text.Should().Contain("desktop_detect");
        text.Should().Contain("desktop_click");
        text.Should().Contain("四叉树");
        text.Should().Contain("look");
        text.Should().Contain("zoom");
    }
}