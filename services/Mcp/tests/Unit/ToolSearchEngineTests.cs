using JoinCode.Abstractions.LLM.Chat;

namespace Mcp.Tests;

public sealed class ToolSearchEngineTests
{
    private static DeferredToolInfo Tool(string name, string? desc = null, bool isMcp = false) =>
        new(name, desc, null, isMcp);

    [Fact]
    public void Search_ExactName_MatchesFirst()
    {
        var engine = new ToolSearchEngine([Tool("read_file"), Tool("write_file")]);
        var result = engine.Search("read_file");
        result.MatchedToolNames.Should().Contain("read_file");
    }

    [Fact]
    public void Search_Keyword_MatchesDescription()
    {
        var engine = new ToolSearchEngine([Tool("tool1", "reads a file"), Tool("tool2", "writes data")]);
        var result = engine.Search("file");
        result.MatchedToolNames.Should().Contain("tool1");
    }

    [Fact]
    public void Search_McpTool_ScoresHigher()
    {
        var engine = new ToolSearchEngine([Tool("read", isMcp: false), Tool("read", isMcp: true)]);
        var result = engine.Search("read");
        result.MatchedToolNames[0].Should().Be("read");
    }

    [Fact]
    public void Search_SelectQuery_ReturnsExactMatches()
    {
        var engine = new ToolSearchEngine([Tool("a"), Tool("b"), Tool("c")]);
        var result = engine.Search("select:a,c");
        result.MatchedToolNames.Should().BeEquivalentTo(["a", "c"]);
    }

    [Fact]
    public void Search_SelectQuery_NoMatch_ReturnsEmpty()
    {
        var engine = new ToolSearchEngine([Tool("a"), Tool("b")]);
        var result = engine.Search("select:x");
        result.MatchedToolNames.Should().BeEmpty();
    }

    [Fact]
    public void Search_MaxResults_LimitsOutput()
    {
        var tools = Enumerable.Range(0, 20).Select(i => Tool($"tool{i}", "match")).ToList();
        var engine = new ToolSearchEngine(tools);
        var result = engine.Search("match", maxResults: 5);
        result.MatchedToolNames.Count.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public void Search_RequiredTerm_PrefixPlus()
    {
        var engine = new ToolSearchEngine([Tool("read_file", "read a file"), Tool("write_file", "write data")]);
        var result = engine.Search("+read file");
        result.MatchedToolNames.Should().Contain("read_file");
        result.MatchedToolNames.Should().NotContain("write_file");
    }

    [Fact]
    public void Search_RequiredTerm_Missing_ExcludesResult()
    {
        var engine = new ToolSearchEngine([Tool("write_file", "write data")]);
        var result = engine.Search("+read data");
        result.MatchedToolNames.Should().NotContain("write_file");
    }

    [Fact]
    public void Search_ThrowsOnNullQuery()
    {
        var engine = new ToolSearchEngine([]);
        Assert.Throws<ArgumentNullException>(() => engine.Search(null!));
    }

    [Fact]
    public void Search_EmptyTools_ReturnsEmpty()
    {
        var engine = new ToolSearchEngine([]);
        var result = engine.Search("anything");
        result.MatchedToolNames.Should().BeEmpty();
    }
}
