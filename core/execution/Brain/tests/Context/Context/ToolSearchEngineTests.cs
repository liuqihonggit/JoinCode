namespace Core.Tests.Context.Context;

public sealed class ToolSearchEngineTests
{
    private static List<DeferredToolInfo> CreateTestTools() =>
    [
        new("mcp.search", "Search files in workspace", isMcp: true),
        new("mcp.read", "Read file contents", isMcp: true),
        new("mcp.write", "Write file contents", isMcp: true),
        new("mcp.notebook", "Execute notebook cells", isMcp: true),
        new("local_helper", "A local helper tool")
    ];

    [Fact]
    public void Search_SelectMode_ExactMatch()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("select:mcp.search");

        Assert.True(result.HasMatches);
        Assert.Contains("mcp.search", result.MatchedToolNames);
    }

    [Fact]
    public void Search_SelectMode_MultipleNames()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("select:mcp.search,mcp.read");

        Assert.Equal(2, result.MatchedToolNames.Count);
    }

    [Fact]
    public void Search_SelectMode_NoMatch()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("select:mcp.nonexistent");

        Assert.False(result.HasMatches);
    }

    [Fact]
    public void Search_Keyword_NameMatch()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("search");

        Assert.True(result.HasMatches);
        Assert.Contains("mcp.search", result.MatchedToolNames);
    }

    [Fact]
    public void Search_Keyword_DescriptionMatch()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("notebook");

        Assert.True(result.HasMatches);
        Assert.Contains("mcp.notebook", result.MatchedToolNames);
    }

    [Fact]
    public void Search_Keyword_ExactNameMatchBeatsContainsMatch()
    {
        var tools = new List<DeferredToolInfo>
        {
            new("search", "Search utility", isMcp: false),
            new("mcp.search", "Search files", isMcp: true)
        };
        var engine = new ToolSearchEngine(tools);

        var result = engine.Search("search");

        Assert.Equal("search", result.MatchedToolNames[0]);
    }

    [Fact]
    public void Search_Keyword_McpNamePartMatchScoresHigherThanDescription()
    {
        var tools = new List<DeferredToolInfo>
        {
            new("mcp.other", "A search utility for files", isMcp: true),
            new("mcp.search", "Search files in workspace", isMcp: true)
        };
        var engine = new ToolSearchEngine(tools);

        var result = engine.Search("search");

        Assert.Equal("mcp.search", result.MatchedToolNames[0]);
    }

    [Fact]
    public void Search_Keyword_PrefixMatch()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("mcp");

        Assert.True(result.HasMatches);
        Assert.True(result.MatchedToolNames.Count >= 4);
    }

    [Fact]
    public void Search_Keyword_RequiredTerm()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("+search file");

        Assert.True(result.HasMatches);
        Assert.Contains("mcp.search", result.MatchedToolNames);
    }

    [Fact]
    public void Search_Keyword_RequiredTermMissing()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("+nonexistent file");

        Assert.False(result.HasMatches);
    }

    [Fact]
    public void Search_MaxResults()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        var result = engine.Search("mcp", maxResults: 2);

        Assert.Equal(2, result.MatchedToolNames.Count);
    }

    [Fact]
    public void Search_EmptyQuery_Throws()
    {
        var engine = new ToolSearchEngine(CreateTestTools());

        Assert.Throws<ArgumentException>(new Action(() => engine.Search(string.Empty)));
    }

    [Fact]
    public void Search_NoDeferredTools()
    {
        var engine = new ToolSearchEngine([]);

        var result = engine.Search("search");

        Assert.False(result.HasMatches);
    }

    [Fact]
    public void Search_NamePartMatch()
    {
        var tools = new List<DeferredToolInfo>
        {
            new("mcp_slack_send", "Send a Slack message", isMcp: true),
            new("mcp_slack_read", "Read Slack messages", isMcp: true)
        };
        var engine = new ToolSearchEngine(tools);

        var result = engine.Search("slack send");

        Assert.True(result.HasMatches);
        Assert.Equal("mcp_slack_send", result.MatchedToolNames[0]);
    }
}
