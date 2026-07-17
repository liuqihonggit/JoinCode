using Api.LLM.CacheProtocol;

namespace Llm.Tests.Adapters.CacheProtocol;

public sealed class AddCacheBreakpointsTests
{
    private readonly AnthropicCacheProtocol _protocol = new();

    [Fact]
    public void AddCacheBreakpoints_PlacesOnLastStaticSystemBlock()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "Static prefix", IsStatic = true },
            new() { Text = "Dynamic content", IsStatic = false }
        };
        var tools = new List<AnthropicToolDefinition> { new() { Name = "read" } };
        var messages = new List<AnthropicMessage>();

        _protocol.AddCacheBreakpoints(systemBlocks, tools, messages, hasMcpTools: false);

        systemBlocks[0].CacheControl.Should().NotBeNull("cache_control on last static system block");
        systemBlocks[1].CacheControl.Should().BeNull("dynamic block should not get cache_control");
    }

    [Fact]
    public void AddCacheBreakpoints_PlacesOnLastTool()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "System", IsStatic = true }
        };
        var tools = new List<AnthropicToolDefinition>
        {
            new() { Name = "read" },
            new() { Name = "write" }
        };
        var messages = new List<AnthropicMessage>();

        _protocol.AddCacheBreakpoints(systemBlocks, tools, messages, hasMcpTools: false);

        tools[0].CacheControl.Should().BeNull("only last tool gets cache_control");
        tools[1].CacheControl.Should().NotBeNull("last tool gets cache_control");
    }

    [Fact]
    public void AddCacheBreakpoints_PlacesOnLastToolResult()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "System", IsStatic = true }
        };
        var tools = new List<AnthropicToolDefinition> { new() { Name = "read" } };
        var messages = new List<AnthropicMessage>
        {
            new()
            {
                Role = "user",
                Content = new List<AnthropicContentBlock>
                {
                    new AnthropicToolResultBlock { ToolUseId = "id1", Content = "result1" },
                    new AnthropicToolResultBlock { ToolUseId = "id2", Content = "result2" }
                }
            }
        };

        _protocol.AddCacheBreakpoints(systemBlocks, tools, messages, hasMcpTools: false);

        var blocks = (List<AnthropicContentBlock>)messages[0].Content!;
        blocks[0].CacheControl.Should().BeNull("only last tool result gets cache_control");
        blocks[1].CacheControl.Should().NotBeNull("last tool result gets cache_control");
    }

    [Fact]
    public void AddCacheBreakpoints_WithMcpTools_AllScopesAreOrg()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "System", IsStatic = true }
        };
        var tools = new List<AnthropicToolDefinition> { new() { Name = "mcp.search" } };
        var messages = new List<AnthropicMessage>();

        _protocol.AddCacheBreakpoints(systemBlocks, tools, messages, hasMcpTools: true);

        systemBlocks[0].CacheControl!.Scope.Should().Be("org");
        tools[0].CacheControl!.Scope.Should().Be("org");
    }

    [Fact]
    public void AddCacheBreakpoints_WithoutMcpTools_ScopesAreNull()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "System", IsStatic = true }
        };
        var tools = new List<AnthropicToolDefinition> { new() { Name = "read" } };
        var messages = new List<AnthropicMessage>();

        _protocol.AddCacheBreakpoints(systemBlocks, tools, messages, hasMcpTools: false);

        systemBlocks[0].CacheControl!.Scope.Should().BeNull();
        tools[0].CacheControl!.Scope.Should().BeNull();
    }

    [Fact]
    public void AddCacheBreakpoints_EmptySystemBlocks_NoSystemCacheControl()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>();
        var tools = new List<AnthropicToolDefinition> { new() { Name = "read" } };
        var messages = new List<AnthropicMessage>();

        _protocol.AddCacheBreakpoints(systemBlocks, tools, messages, hasMcpTools: false);

        tools[0].CacheControl.Should().NotBeNull("tools still get cache_control even without system blocks");
    }

    [Fact]
    public void AddCacheBreakpoints_EmptyTools_NoToolCacheControl()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "System", IsStatic = true }
        };
        var tools = new List<AnthropicToolDefinition>();
        var messages = new List<AnthropicMessage>();

        _protocol.AddCacheBreakpoints(systemBlocks, tools, messages, hasMcpTools: false);

        systemBlocks[0].CacheControl.Should().NotBeNull("system blocks still get cache_control even without tools");
    }

    [Fact]
    public void AddCacheBreakpoints_MultipleMessagesWithToolResults_LastResultGetsCacheControl()
    {
        var systemBlocks = new List<AnthropicSystemContentBlock>
        {
            new() { Text = "System", IsStatic = true }
        };
        var tools = new List<AnthropicToolDefinition> { new() { Name = "read" } };
        var messages = new List<AnthropicMessage>
        {
            new()
            {
                Role = "user",
                Content = new List<AnthropicContentBlock>
                {
                    new AnthropicToolResultBlock { ToolUseId = "id1", Content = "result1" }
                }
            },
            new()
            {
                Role = "user",
                Content = new List<AnthropicContentBlock>
                {
                    new AnthropicToolResultBlock { ToolUseId = "id2", Content = "result2" }
                }
            }
        };

        _protocol.AddCacheBreakpoints(systemBlocks, tools, messages, hasMcpTools: false);

        var blocks1 = (List<AnthropicContentBlock>)messages[0].Content!;
        var blocks2 = (List<AnthropicContentBlock>)messages[1].Content!;
        blocks1[0].CacheControl.Should().BeNull("only last tool result across all messages gets cache_control");
        blocks2[0].CacheControl.Should().NotBeNull("last tool result gets cache_control");
    }
}
