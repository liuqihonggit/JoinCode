// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003

namespace JoinCode.Hands.Scenarios.Tests;

/// <summary>
/// ScenarioRegistry 单元测试 — 验证源码生成器从 [Scenario] 特性正确收集全部情景模式
/// </summary>
public sealed class ScenarioRegistryTests {
    /// <summary>全部8个情景模式及其预期工具集 — 供参数化测试</summary>
    public static IEnumerable<object[]> AllScenarios => new[] {
        new object[] { "build", new[] { "build_queue_status", "build_cancel" } },
        new object[] { "desktop", new[] { "desktop_look", "desktop_zoom", "desktop_detect", "desktop_click", "desktop_type", "desktop_drag" } },
        new object[] { "environment", new[] { "get_environment_state", "wait_for_idle", "undo_last_action", "get_operation_history" } },
        new object[] { "error_fix", new[] { "diagnose_error", "fix_file_error", "fix_shell_error", "fix_merge_conflict" } },
        new object[] { "macro", new[] { "start_recording", "stop_recording", "play_macro", "list_macros" } },
        new object[] { "observation", new[] { "start_observation", "learn_from_observation", "optimize_steps", "reproduce_from_logic" } },
        new object[] { "process", new[] { "list_processes", "start_process", "wait_for_idle", "kill_process" } },
        new object[] { "window", new[] { "list_windows", "focus_window", "move_window", "close_window", "screenshot" } },
    };

    /// <summary>每个情景模式应注册到 ScenarioRegistry 且含预期工具集 + 非空流程/描述</summary>
    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void Find_ShouldReturnScenarioWithExpectedTools(string name, string[] expectedTools) {
        var scenario = ScenarioRegistry.Find(name);
        scenario.Should().NotBeNull($"scenario '{name}' should be registered");
        scenario!.Tools.Should().Contain(expectedTools);
        scenario.SuggestedFlow.Should().NotBeEmpty($"scenario '{name}' should have suggested flow");
        scenario.Description.Should().NotBeEmpty($"scenario '{name}' should have description");
    }

    /// <summary>ScenarioRegistry.Scenarios 应含全部8个情景模式</summary>
    [Fact]
    public void Scenarios_ShouldContainAllEightScenarios() {
        ScenarioRegistry.Scenarios.Should().HaveCount(8);
        ScenarioRegistry.Scenarios.Select(s => s.Name).Should().Contain(new[] {
            "build", "desktop", "environment", "error_fix", "macro", "observation", "process", "window"
        });
    }

    /// <summary>desktop 情景模式含四叉树描述 + look→zoom→detect→click 流程</summary>
    [Fact]
    public void Find_Desktop_HasQuadtreeDescriptionAndFlow() {
        var scenario = ScenarioRegistry.Find("desktop");
        scenario.Should().NotBeNull();
        scenario!.Description.Should().Contain("四叉树");
        scenario.SuggestedFlow.Should().Contain("look");
        scenario.SuggestedFlow.Should().Contain("zoom");
        scenario.SuggestedFlow.Should().Contain("detect");
        scenario.SuggestedFlow.Should().Contain("click");
        scenario.Tips.Should().NotBeEmpty();
    }

    /// <summary>Find 未知名返回 null</summary>
    [Fact]
    public void Find_UnknownName_ReturnsNull() {
        ScenarioRegistry.Find("nonexistent_scenario").Should().BeNull();
    }
}
