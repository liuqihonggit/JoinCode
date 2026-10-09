// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Mcp.Tests;

public class McpPromptTemplatesTests {
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