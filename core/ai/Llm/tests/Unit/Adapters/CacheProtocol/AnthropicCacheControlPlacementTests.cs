using Api.LLM.CacheProtocol;

namespace Llm.Tests.Adapters.CacheProtocol;

public sealed class AnthropicCacheControlPlacementTests
{
    private readonly AnthropicCacheProtocol _protocol = new();

    [Fact]
    public void PlaceCacheControlOnSystemBlocks_LastStaticBlock_GetsCacheControl()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "Static prefix", IsStatic = true },
            new() { Text = "Dynamic content", IsStatic = false }
        };

        _protocol.PlaceCacheControlOnSystemBlocks(systemBlocks, hasMcpTools: false);

        systemBlocks[0].CacheControl.Should().NotBeNull(
            "cache_control should be placed on the last static system block");
        systemBlocks[1].CacheControl.Should().BeNull(
            "cache_control should NOT be placed on dynamic system blocks");
    }

    [Fact]
    public void PlaceCacheControlOnSystemBlocks_AllDynamic_LastBlock_GetsCacheControl()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "Dynamic 1", IsStatic = false },
            new() { Text = "Dynamic 2", IsStatic = false }
        };

        _protocol.PlaceCacheControlOnSystemBlocks(systemBlocks, hasMcpTools: false);

        systemBlocks[0].CacheControl.Should().BeNull();
        systemBlocks[1].CacheControl.Should().NotBeNull(
            "when no static blocks exist, cache_control should be placed on the last system block");
    }

    [Fact]
    public void PlaceCacheControlOnSystemBlocks_WithMcpTools_ScopeShouldBeOrg()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "Static prefix", IsStatic = true }
        };

        _protocol.PlaceCacheControlOnSystemBlocks(systemBlocks, hasMcpTools: true);

        systemBlocks[0].CacheControl.Should().NotBeNull();
        systemBlocks[0].CacheControl!.Scope.Should().Be("org",
            "with MCP tools, system block cache_control should use org scope");
    }

    [Fact]
    public void PlaceCacheControlOnTools_LastTool_GetsCacheControl()
    {
        var tools = new List<AnthropicToolDefinition>
        {
            new() { Name = "read" },
            new() { Name = "write" }
        };

        _protocol.PlaceCacheControlOnTools(tools, hasMcpTools: false);

        tools[0].CacheControl.Should().BeNull(
            "cache_control should only be on the last tool");
        tools[1].CacheControl.Should().NotBeNull(
            "cache_control should be on the last tool definition");
    }

    [Fact]
    public void PlaceCacheControlOnTools_WithMcpTools_ScopeShouldBeOrg()
    {
        var tools = new List<AnthropicToolDefinition>
        {
            new() { Name = "mcp.search" }
        };

        _protocol.PlaceCacheControlOnTools(tools, hasMcpTools: true);

        tools[0].CacheControl.Should().NotBeNull();
        tools[0].CacheControl!.Scope.Should().Be("org",
            "with MCP tools, tool cache_control should use org scope");
    }

    [Fact]
    public void PlaceCacheControlOnToolResults_LastResult_GetsCacheControl()
    {
        var toolResults = new List<AnthropicToolResultBlock>
        {
            new() { ToolUseId = "id1", Content = "result1" },
            new() { ToolUseId = "id2", Content = "result2" }
        };

        _protocol.PlaceCacheControlOnToolResults(toolResults, hasMcpTools: false);

        toolResults[0].CacheControl.Should().BeNull(
            "cache_control should only be on the last tool result");
        toolResults[1].CacheControl.Should().NotBeNull(
            "cache_control should be on the last tool result block");
    }

    [Fact]
    public void PlaceCacheControlOnToolResults_WithMcpTools_ScopeShouldBeOrg()
    {
        var toolResults = new List<AnthropicToolResultBlock>
        {
            new() { ToolUseId = "id1", Content = "result" }
        };

        _protocol.PlaceCacheControlOnToolResults(toolResults, hasMcpTools: true);

        toolResults[0].CacheControl.Should().NotBeNull();
        toolResults[0].CacheControl!.Scope.Should().Be("org",
            "with MCP tools, tool result cache_control should use org scope — " +
            "previously FlushToolResultsAsUserMessage always passed hasMcpTools=false (fixed)");
    }

    [Fact]
    public void PlaceCacheControlOnToolResults_WithoutMcpTools_ScopeShouldBeNull()
    {
        var toolResults = new List<AnthropicToolResultBlock>
        {
            new() { ToolUseId = "id1", Content = "result" }
        };

        _protocol.PlaceCacheControlOnToolResults(toolResults, hasMcpTools: false);

        toolResults[0].CacheControl.Should().NotBeNull();
        toolResults[0].CacheControl!.Scope.Should().BeNull(
            "without MCP tools, tool result cache_control should not specify scope");
    }
}
