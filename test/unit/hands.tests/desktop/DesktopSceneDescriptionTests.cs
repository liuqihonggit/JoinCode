namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// 场景说明验证 — AC-13 menu 含完整场景说明保留给 AI
/// </summary>
public sealed class DesktopSceneDescriptionTests
{
    /// <summary>AC-13: menu 含四叉树夹逼法说明 + 看不清/点错提示 + 建议流程</summary>
    [Fact]
    public async Task SceneMenu_ContainsFullSceneDescription()
    {
        var handler = new DesktopSceneMenuToolHandlers();
        var result = await handler.SceneMenuAsync();
        var text = result.GetFirstText();

        text.Should().Contain("四叉树");
        text.Should().Contain("夹逼");
        text.Should().Contain("看不清");
        text.Should().Contain("zoom");
        text.Should().Contain("detect");
        text.Should().Contain("click");
        text.Should().Contain("look");
        text.Should().Contain("desktop_control");
    }

    /// <summary>AC-13: menu 含工具集完整列表</summary>
    [Fact]
    public async Task SceneMenu_ContainsAllTools()
    {
        var handler = new DesktopSceneMenuToolHandlers();
        var result = await handler.SceneMenuAsync();
        var text = result.GetFirstText();

        text.Should().Contain("desktop_look");
        text.Should().Contain("desktop_zoom");
        text.Should().Contain("desktop_detect");
        text.Should().Contain("desktop_click");
        text.Should().Contain("desktop_type");
        text.Should().Contain("desktop_drag");
    }
}
