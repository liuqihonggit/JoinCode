namespace JoinCode.Hands.Scenarios.Tests;

/// <summary>
/// ToolMenuRenderer 单元测试 — 验证统一渲染器 ToJson/ToText 输出格式
/// </summary>
public sealed class ToolMenuRendererTests {
    private static readonly ScenarioInfo Sample = new(
        "test", "测试描述", new[] { "tool_a", "tool_b" }, "a → b", "测试提示");

    /// <summary>ToJson 生成含 scenes 数组 + name/description/tools/suggested_flow/tips 的 JSON</summary>
    [Fact]
    public void ToJson_ProducesValidScenarioMenuJson() {
        var json = ToolMenuRenderer.ToJson(Sample);

        json.Should().Contain("\"scenes\"");
        json.Should().Contain("\"name\":\"test\"");
        json.Should().Contain("\"description\":\"测试描述\"");
        json.Should().Contain("\"tool_a\"");
        json.Should().Contain("\"tool_b\"");
        json.Should().Contain("\"suggested_flow\":\"a → b\"");
        json.Should().Contain("\"tips\":\"测试提示\"");
    }

    /// <summary>ToText 生成含情景模式名 + 工具集 + 建议流程 + 提示的多行文本</summary>
    [Fact]
    public void ToText_ProducesHumanReadableText() {
        var text = ToolMenuRenderer.ToText(Sample);

        text.Should().Contain("情景模式: test");
        text.Should().Contain("测试描述");
        text.Should().Contain("工具集:");
        text.Should().Contain("- tool_a");
        text.Should().Contain("- tool_b");
        text.Should().Contain("建议流程: a → b");
        text.Should().Contain("提示: 测试提示");
    }

    /// <summary>ToText 当 Tips 为空时不输出提示行</summary>
    [Fact]
    public void ToText_EmptyTips_OmitsTipsLine() {
        var noTips = new ScenarioInfo("x", "d", [], "f", "");
        var text = ToolMenuRenderer.ToText(noTips);

        text.Should().NotContain("提示:");
    }
}
