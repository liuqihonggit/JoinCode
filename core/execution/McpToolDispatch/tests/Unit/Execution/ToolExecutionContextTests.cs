namespace McpToolRegistry.Tests;

public class ToolExecutionContextTests
{
    [Fact]
    public void IsShortCircuited_NoResult_ReturnsFalse()
    {
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = []
        };

        context.IsShortCircuited.Should().BeFalse();
    }

    [Fact]
    public void IsShortCircuited_WithResult_ReturnsTrue()
    {
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = [],
            Result = new ToolResult
            {
                Content = [new ToolContent { Type = ToolContentType.Text, Text = "done" }],
                IsError = false
            }
        };

        context.IsShortCircuited.Should().BeTrue();
    }

    [Fact]
    public void Arguments_CanBeModified()
    {
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = []
        };

        context.Arguments["key"] = JsonSerializer.SerializeToElement("value");
        context.Arguments.Should().ContainKey("key");
    }

    [Fact]
    public void AgentMode_DefaultsToAuto()
    {
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = []
        };

        context.AgentMode.Should().Be(PermissionMode.Auto);
    }
}
