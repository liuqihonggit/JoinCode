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

    private static DeferredToolInfo GroupedTool(string name, string category, string? groupName, string? desc = null) =>
        new(name, desc, null, isMcp: true, category, groupName);

    [Fact]
    public void Search_MapCategory_ListsAllToolsInCategory()
    {
        var engine = new ToolSearchEngine([
            GroupedTool("file_read", "file", "io"),
            GroupedTool("file_write", "file", "io"),
            GroupedTool("web_fetch", "web", "http"),
        ]);
        var result = engine.Search("map[file]");
        result.MatchedToolNames.Should().BeEquivalentTo(["file_read", "file_write"]);
    }

    [Fact]
    public void Search_MapCategoryGroup_ListsToolsInGroup()
    {
        var engine = new ToolSearchEngine([
            GroupedTool("file_read", "file", "io"),
            GroupedTool("file_write", "file", "io"),
            GroupedTool("web_fetch", "file", "http"),
        ]);
        var result = engine.Search("map[file][io]");
        result.MatchedToolNames.Should().BeEquivalentTo(["file_read", "file_write"]);
    }

    [Fact]
    public void Search_MapCategoryGroupTool_ExactMatch()
    {
        var engine = new ToolSearchEngine([
            GroupedTool("file_read", "file", "io"),
            GroupedTool("file_write", "file", "io"),
        ]);
        var result = engine.Search("map[file][io][file_read]");
        result.MatchedToolNames.Should().BeEquivalentTo(["file_read"]);
    }

    [Fact]
    public void Search_MapNoMatch_ReturnsEmpty()
    {
        var engine = new ToolSearchEngine([GroupedTool("file_read", "file", "io")]);
        var result = engine.Search("map[web]");
        result.MatchedToolNames.Should().BeEmpty();
    }

    [Fact]
    public void Search_ListGroups_ReturnsDistinctHierarchy()
    {
        var engine = new ToolSearchEngine([
            GroupedTool("file_read", "file", "io"),
            GroupedTool("file_write", "file", "io"),
            GroupedTool("web_fetch", "web", "http"),
            GroupedTool("web_open", "web", null),
        ]);
        var result = engine.Search("list_groups");
        result.MatchedToolNames.Should().BeEquivalentTo(["file/io", "web/http", "web"]);
    }
}
