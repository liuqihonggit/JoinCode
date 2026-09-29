namespace Mcp.Tests;

/// <summary>
/// McpToolCollapseClassifier 单元测试 — 验证工具折叠分类(camelCase/kebab/snake 命名 + Search/Read 白名单交集)
/// </summary>
public sealed class McpToolCollapseClassifierTests {
    [Theory]
    [InlineData("search_code", true, false)]
    [InlineData("search-code", true, false)]
    [InlineData("searchCode", true, false)]
    [InlineData("web_search", true, false)]
    [InlineData("webSearch", true, false)]
    [InlineData("web-search", true, false)]
    [InlineData("search", true, false)]
    [InlineData("find", true, false)]
    [InlineData("lookup", true, false)]
    public void Classify_SearchTools_VariousNaming(string toolName, bool expectSearch, bool expectRead) {
        var result = McpToolCollapseClassifier.Classify(toolName);
        result.IsSearch.Should().Be(expectSearch);
        result.IsRead.Should().Be(expectRead);
    }

    [Theory]
    [InlineData("get", false, true)]
    [InlineData("get_file", false, true)]
    [InlineData("getFile", false, true)]
    [InlineData("get-file", false, true)]
    [InlineData("read", false, true)]
    [InlineData("read_file", false, true)]
    [InlineData("list", false, true)]
    [InlineData("list_files", false, true)]
    [InlineData("fetch", false, true)]
    [InlineData("describe", false, true)]
    [InlineData("show", false, true)]
    [InlineData("view", false, true)]
    [InlineData("info", false, true)]
    public void Classify_ReadTools_VariousNaming(string toolName, bool expectSearch, bool expectRead) {
        var result = McpToolCollapseClassifier.Classify(toolName);
        result.IsSearch.Should().Be(expectSearch);
        result.IsRead.Should().Be(expectRead);
    }

    [Fact]
    public void Classify_Query_InBothSearchAndRead() {
        // query 同时在 SearchTools 和 ReadTools 白名单中
        var result = McpToolCollapseClassifier.Classify("query");
        result.IsSearch.Should().BeTrue();
        result.IsRead.Should().BeTrue();
    }

    [Fact]
    public void Classify_UnknownTool_NeitherSearchNorRead() {
        var result = McpToolCollapseClassifier.Classify("unknownTool");
        result.IsSearch.Should().BeFalse();
        result.IsRead.Should().BeFalse();
    }

    [Fact]
    public void Classify_CamelCaseNormalizesToSnakeCase() {
        // searchRepositories → search_repositories → 在 SearchTools 中
        var result = McpToolCollapseClassifier.Classify("searchRepositories");
        result.IsSearch.Should().BeTrue();
    }

    [Fact]
    public void Classify_EmptyString_Throws() {
        var act = () => McpToolCollapseClassifier.Classify(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Classify_NullString_Throws() {
        var act = () => McpToolCollapseClassifier.Classify(null!);
        act.Should().Throw<ArgumentException>();
    }
}
