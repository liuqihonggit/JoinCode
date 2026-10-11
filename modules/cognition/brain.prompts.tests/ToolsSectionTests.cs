namespace Core.Tests.Prompts;

/// <summary>
/// ToolsSection 单元测试 — 验证双向人格压制：禁止滥用 Bash + 鼓励合理使用（GAP-039-02）
/// </summary>
public sealed class ToolsSectionTests {
    [Fact]
    public void GetContent_ShouldContainBashAbuseWarning() {
        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("不要滥用");
    }

    [Fact]
    public void GetContent_ShouldContainReverseSuppression_EncourageBashWhenNoDedicatedTool() {
        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("专用工具无法覆盖");
    }

    [Fact]
    public void GetContent_ShouldContainBothDirections_ForwardAndReverse() {
        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("不要滥用", "正向压制：禁止滥用 Bash");
        content.Should().Contain("专用工具无法覆盖", "反向压制：鼓励专用工具无法覆盖时使用 Bash");
    }
}
