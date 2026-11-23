// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Clock.Tests;

public class ClockPromptTemplatesTests {
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void GetAllTemplates_ContainsContinuation() {
        var templates = ClockPromptTemplates.GetAllTemplates().ToList();

        var continuation = templates.FirstOrDefault(t => t.Name == "continuation");
        continuation.Should().NotBeNull();
        continuation!.Category.Should().Be("Goal");
        continuation.HasParameters.Should().BeTrue();
    }

    [Fact]
    public void GetContent_ReturnsNullForParameterizedTemplate() {
        var content = ClockPromptTemplates.GetContent("continuation");

        content.Should().BeNull();
    }
}