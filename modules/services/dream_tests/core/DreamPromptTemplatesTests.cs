// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Dream.Tests;

public class DreamPromptTemplatesTests {
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void GetAllTemplates_ContainsConsolidation() {
        var templates = DreamPromptTemplates.GetAllTemplates().ToList();

        var consolidation = templates.FirstOrDefault(t => t.Name == "consolidation");
        consolidation.Should().NotBeNull();
        consolidation!.Category.Should().Be("Dream");
        consolidation.HasParameters.Should().BeTrue();
    }

    [Fact]
    public void GetContent_ReturnsNullForParameterizedTemplate() {
        var content = DreamPromptTemplates.GetContent("consolidation");

        content.Should().BeNull();
    }
}