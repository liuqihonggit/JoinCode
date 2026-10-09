namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// GAP-041-04 通用 DAG 蜘蛛网化解框架单元测试 — 验证情景子图解析、蜘蛛网边过滤
/// </summary>
public sealed class ToolScenarioSubgraphResolverTest {
    private static ScenarioInfo CreateScenario(string name, params string[] tools) =>
        new(name, $"Test scenario {name}", tools, "flow", "tips");

    [Fact]
    public void Resolve_ReturnsSubgraphWithScenarioTools() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("file_ops",
            FileToolName.FileRead.ToValue(), FileToolName.FileEdit.ToValue(), FileToolName.FileWrite.ToValue());

        var subgraph = resolver.Resolve(scenario);

        subgraph.ScenarioName.Should().Be("file_ops");
        subgraph.Tools.Should().Contain(FileToolName.FileRead.ToValue());
        subgraph.Tools.Should().Contain(FileToolName.FileEdit.ToValue());
    }

    [Fact]
    public void Resolve_IncludesEdges_WhenAllMembersInScenario() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("file_ops",
            FileToolName.FileRead.ToValue(), FileToolName.FileEdit.ToValue(), FileToolName.FileWrite.ToValue(), FileToolName.FileDelete.ToValue());

        var subgraph = resolver.Resolve(scenario);

        subgraph.Edges.Should().NotBeEmpty();
        subgraph.Edges.Any(e => e.Id == "file_ops").Should().BeTrue();
    }

    [Fact]
    public void Resolve_FiltersSpiderWebEdges_WhenNotAllMembersInScenario() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("partial_file",
            FileToolName.FileRead.ToValue(), FileToolName.FileEdit.ToValue());

        var subgraph = resolver.Resolve(scenario);

        subgraph.FilteredEdgeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Resolve_NoEdges_WhenScenarioHasUnknownTools() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("unknown", "nonexistent_tool_1", "nonexistent_tool_2");

        var subgraph = resolver.Resolve(scenario);

        subgraph.Edges.Should().BeEmpty();
        subgraph.FilteredEdgeCount.Should().Be(0);
    }

    [Fact]
    public void GetScopedChainRecommendations_FiltersToolsOutsideScenario() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("file_ops",
            FileToolName.FileRead.ToValue(), FileToolName.FileEdit.ToValue());

        var chain = resolver.GetScopedChainRecommendations(scenario, FileToolName.FileRead.ToValue());

        chain.Should().Contain(FileToolName.FileEdit.ToValue());
    }

    [Fact]
    public void GetScopedChainRecommendations_ReturnsEmpty_WhenToolNotInScenario() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("git_only",
            GitToolName.GitStatus.ToValue(), GitToolName.GitDiff.ToValue());

        var chain = resolver.GetScopedChainRecommendations(scenario, FileToolName.FileRead.ToValue());

        chain.Should().BeEmpty();
    }

    [Fact]
    public void IsToolInScenario_True_WhenToolBelongsToScenario() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("file_ops",
            FileToolName.FileRead.ToValue(), FileToolName.FileEdit.ToValue());

        resolver.IsToolInScenario(scenario, FileToolName.FileRead.ToValue()).Should().BeTrue();
    }

    [Fact]
    public void IsToolInScenario_False_WhenToolNotInScenario() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("git_only",
            GitToolName.GitStatus.ToValue());

        resolver.IsToolInScenario(scenario, FileToolName.FileRead.ToValue()).Should().BeFalse();
    }

    [Fact]
    public void Resolve_GitScenario_IncludesGitChainEdges() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("git_full",
            GitToolName.GitStatus.ToValue(), GitToolName.GitAdd.ToValue(),
            GitToolName.GitCommit.ToValue(), GitToolName.GitPush.ToValue(),
            GitToolName.GitDiff.ToValue());

        var subgraph = resolver.Resolve(scenario);

        subgraph.Edges.Any(e => e.Id == "git_chain").Should().BeTrue();
    }

    [Fact]
    public void Resolve_PartialGitScenario_FiltersGitChainEdge() {
        using var scorer = new ToolHypergraphScorer();
        var resolver = new ToolScenarioSubgraphResolver(scorer);
        var scenario = CreateScenario("git_partial",
            GitToolName.GitStatus.ToValue(), GitToolName.GitDiff.ToValue());

        var subgraph = resolver.Resolve(scenario);

        subgraph.Edges.Any(e => e.Id == "git_chain").Should().BeFalse();
        subgraph.FilteredEdgeCount.Should().BeGreaterThan(0);
    }
}
