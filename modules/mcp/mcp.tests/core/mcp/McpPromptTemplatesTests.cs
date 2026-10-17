// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Mcp.Tests;

public class McpPromptTemplatesTests {
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void GetAllTemplates_ContainsMcpWorkflow() {
        var templates = McpPromptTemplates.GetAllTemplates().ToList();

        var mcpWorkflow = templates.FirstOrDefault(t => t.Name == "mcp_workflow");
        mcpWorkflow.Should().NotBeNull();
        mcpWorkflow!.Category.Should().Be("Mcp");
        mcpWorkflow.HasParameters.Should().BeTrue();
    }

    [Fact]
    public void GetContent_ReturnsNullForParameterizedTemplate() {
        var content = McpPromptTemplates.GetContent("mcp_workflow");

        content.Should().BeNull();
    }
}