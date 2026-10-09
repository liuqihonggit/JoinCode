namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopSceneMenuToolHandlers 单元测试 — AC-01 场景菜单可被发现 + IToolMenu 接口
/// </summary>
public sealed class DesktopSceneMenuToolHandlersTests {
    /// <summary>AC-01: 调用 desktop_scene_menu 返回含 desktop 场景 + 工具集 + 建议流程</summary>
    [Fact]
    public async Task SceneMenu_ReturnsDesktopScene_WithToolsAndFlow() {
        var handler = new DesktopSceneMenuToolHandlers();
        var result = await handler.SceneMenuAsync();

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().NotBeNull();
        text.Should().Contain("desktop");
        text.Should().Contain("desktop_look");
        text.Should().Contain("desktop_zoom");
        text.Should().Contain("desktop_detect");
        text.Should().Contain("desktop_click");
        text.Should().Contain("四叉树");
        text.Should().Contain("look");
        text.Should().Contain("zoom");
    }

    /// <summary>AC-02: IToolMenu.GetScenario() 返回 ScenarioRegistry 中注册的 desktop 情景模式</summary>
    [Fact]
    public void GetScenario_ReturnsDesktopScenarioFromRegistry() {
        var handler = new DesktopSceneMenuToolHandlers();
        var scenario = handler.GetScenario();

        scenario.Name.Should().Be("desktop");
        scenario.Tools.Should().Contain("desktop_click");
        scenario.SuggestedFlow.Should().Contain("look");
    }
}