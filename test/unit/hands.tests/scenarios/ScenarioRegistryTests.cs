namespace JoinCode.Hands.Scenarios.Tests;

/// <summary>
/// ScenarioRegistry 单元测试 — 验证源码生成器从 [Scenario] 特性正确收集情景模式
/// </summary>
public sealed class ScenarioRegistryTests {
    /// <summary>ScenarioRegistry.Scenarios 非空 — 至少含 desktop 情景模式</summary>
    [Fact]
    public void Scenarios_ShouldContainDesktopScenario() {
        ScenarioRegistry.Scenarios.Should().NotBeEmpty();
        ScenarioRegistry.Scenarios.Should().Contain(s => s.Name == "desktop");
    }

    /// <summary>Find("desktop") 返回含正确工具集 + 建议流程 + 提示的 ScenarioInfo</summary>
    [Fact]
    public void Find_Desktop_ReturnsScenarioWithToolsAndFlow() {
        var scenario = ScenarioRegistry.Find("desktop");
        scenario.Should().NotBeNull();
        scenario!.Tools.Should().Contain(new[] { "desktop_look", "desktop_zoom", "desktop_detect", "desktop_click", "desktop_type", "desktop_drag" });
        scenario.SuggestedFlow.Should().Contain("look");
        scenario.SuggestedFlow.Should().Contain("zoom");
        scenario.SuggestedFlow.Should().Contain("detect");
        scenario.SuggestedFlow.Should().Contain("click");
        scenario.Tips.Should().NotBeEmpty();
        scenario.Description.Should().Contain("四叉树");
    }

    /// <summary>Find 未知名返回 null</summary>
    [Fact]
    public void Find_UnknownName_ReturnsNull() {
        ScenarioRegistry.Find("nonexistent_scenario").Should().BeNull();
    }
}
