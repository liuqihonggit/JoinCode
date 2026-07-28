namespace Core.Tests.Context.Context;

public sealed class ToolReferenceExtractorTests
{
    [Fact]
    public void Extract_FromMessageList_WithToolReferences()
    {
        var history = new MessageList();
        history.Add(new ApiMessage(MessageRole.User, "hello"));
        history.Add(new ApiMessage(MessageRole.Tool, "tool result")
        {
            Metadata = new Dictionary<string, JsonElement>
            {
                ["ToolReferences"] = JsonElementHelper.FromJson("[\"mcp.search\",\"mcp.read\"]")
            }
        });

        var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(history);

        Assert.Equal(2, discovered.Count);
        Assert.Contains("mcp.search", discovered);
        Assert.Contains("mcp.read", discovered);
    }

    [Fact]
    public void Extract_FromMessageList_NoToolReferences()
    {
        var history = new MessageList();
        history.Add(new ApiMessage(MessageRole.User, "hello"));
        history.Add(new ApiMessage(MessageRole.Assistant, "hi"));

        var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(history);

        Assert.Empty(discovered);
    }

    [Fact]
    public void Extract_FromMessages_WithToolReferences()
    {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.Tool, "result")
            {
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["ToolReferences"] = JsonElementHelper.FromJson("[\"mcp.write\"]")
                }
            }
        };

        var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(messages);

        Assert.Single(discovered);
        Assert.Contains("mcp.write", discovered);
    }

    [Fact]
    public void Extract_SkipsNonToolMessages()
    {
        var history = new MessageList();
        history.Add(new ApiMessage(MessageRole.User, "hello"));
        history.Add(new ApiMessage(MessageRole.Assistant, "hi"));
        history.Add(new ApiMessage(MessageRole.System, "system"));

        var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(history);

        Assert.Empty(discovered);
    }

    [Fact]
    public void Extract_MultipleToolResults_Accumulates()
    {
        var history = new MessageList();
        history.Add(new ApiMessage(MessageRole.Tool, "result1")
        {
            Metadata = new Dictionary<string, JsonElement>
            {
                ["ToolReferences"] = JsonElementHelper.FromJson("[\"mcp.search\"]")
            }
        });
        history.Add(new ApiMessage(MessageRole.Tool, "result2")
        {
            Metadata = new Dictionary<string, JsonElement>
            {
                ["ToolReferences"] = JsonElementHelper.FromJson("[\"mcp.read\"]")
            }
        });

        var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(history);

        Assert.Equal(2, discovered.Count);
    }

    [Fact]
    public void Extract_DuplicateToolNames_Deduplicates()
    {
        var history = new MessageList();
        history.Add(new ApiMessage(MessageRole.Tool, "result1")
        {
            Metadata = new Dictionary<string, JsonElement>
            {
                ["ToolReferences"] = JsonElementHelper.FromJson("[\"mcp.search\"]")
            }
        });
        history.Add(new ApiMessage(MessageRole.Tool, "result2")
        {
            Metadata = new Dictionary<string, JsonElement>
            {
                ["ToolReferences"] = JsonElementHelper.FromJson("[\"mcp.search\"]")
            }
        });

        var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(history);

        Assert.Single(discovered);
    }
}
