namespace Sync.Tests.Agents;

public sealed class AgentDefinitionProviderTests
{
    [Fact]
    public void GetBuiltInDefinitions_ReturnsFiveAgentTypes()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();

        definitions.Should().HaveCount(5);
        definitions.Select(d => d.AgentType).Should().Contain(["default", "code", "search", "Explore", "Plan"]);
    }

    [Fact]
    public void GetBuiltInDefinitions_DefaultAgent_HasSubAgentDisallowedTools()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var defaultAgent = definitions.First(d => d.AgentType == "default");

        defaultAgent.Tools.Should().BeNull();
        defaultAgent.DisallowedTools.Should().NotBeNull();
        defaultAgent.DisallowedTools.Should().Contain([
            AgentToolNameConstants.Agent, AgentToolNameConstants.AgentSpawn
        ]);
    }

    [Fact]
    public void GetBuiltInDefinitions_CodeAgent_UsesToolNamesConstants()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var codeAgent = definitions.First(d => d.AgentType == "code");

        codeAgent.Tools.Should().NotBeNull();
        codeAgent.Tools.Should().Contain([
            FileToolNameConstants.FileRead,
            FileToolNameConstants.FileWrite,
            FileToolNameConstants.FileEdit,
            SearchToolNameConstants.Glob,
            SearchToolNameConstants.Grep,
            ShellToolNameConstants.Bash,
            SearchToolNameConstants.SearchCodebase
        ]);
        codeAgent.DisallowedTools.Should().NotBeNull();
        codeAgent.DisallowedTools.Should().Contain([
            AgentToolNameConstants.Agent, AgentToolNameConstants.AgentSpawn
        ]);
    }

    [Fact]
    public void GetBuiltInDefinitions_SearchAgent_UsesToolNamesConstants()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var searchAgent = definitions.First(d => d.AgentType == "search");

        searchAgent.Tools.Should().NotBeNull();
        searchAgent.Tools.Should().Contain([
            FileToolNameConstants.FileRead,
            SearchToolNameConstants.Glob,
            SearchToolNameConstants.Grep,
            SearchToolNameConstants.SearchCodebase
        ]);

        searchAgent.DisallowedTools.Should().NotBeNull();
        searchAgent.DisallowedTools.Should().Contain([
            FileToolNameConstants.FileWrite,
            FileToolNameConstants.FileEdit,
            ShellToolNameConstants.Bash
        ]);
    }

    [Fact]
    public void GetBuiltInDefinitions_CodeAgent_DisallowedToolsContainsSubAgentTools()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var codeAgent = definitions.First(d => d.AgentType == "code");

        codeAgent.DisallowedTools.Should().NotBeNull();
        codeAgent.DisallowedTools.Should().Contain([
            AgentToolNameConstants.Agent, AgentToolNameConstants.AgentSpawn
        ]);
    }

    [Fact]
    public void GetBuiltInDefinitions_SearchAgent_DisallowedWriteTools()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var searchAgent = definitions.First(d => d.AgentType == "search");

        searchAgent.DisallowedTools.Should().NotBeNullOrEmpty();
        searchAgent.DisallowedTools.Should().Contain(FileToolNameConstants.FileWrite);
        searchAgent.DisallowedTools.Should().Contain(FileToolNameConstants.FileEdit);
        searchAgent.DisallowedTools.Should().Contain(ShellToolNameConstants.Bash);
    }

    [Fact]
    public void GetBuiltInDefinitions_ExploreAgent_IsReadOnly()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var exploreAgent = definitions.First(d => d.AgentType == "Explore");

        exploreAgent.Tools.Should().NotBeNull();
        exploreAgent.Tools.Should().Contain([FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.Bash]);
        exploreAgent.DisallowedTools.Should().NotBeNull();
        exploreAgent.DisallowedTools.Should().Contain([AgentToolNameConstants.Agent, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite]);
        exploreAgent.IsBackground.Should().BeFalse();
    }

    [Fact]
    public void GetBuiltInDefinitions_PlanAgent_IsReadOnly()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var planAgent = definitions.First(d => d.AgentType == "Plan");

        planAgent.Tools.Should().NotBeNull();
        planAgent.Tools.Should().Contain([FileToolNameConstants.FileRead, SearchToolNameConstants.Glob, SearchToolNameConstants.Grep, SearchToolNameConstants.SearchCodebase, ShellToolNameConstants.Bash]);
        planAgent.DisallowedTools.Should().NotBeNull();
        planAgent.DisallowedTools.Should().Contain([AgentToolNameConstants.Agent, FileToolNameConstants.FileEdit, FileToolNameConstants.FileWrite]);
        planAgent.IsBackground.Should().BeFalse();
    }

    [Fact]
    public void GetBuiltInDefinitions_AllToolNames_AreValidToolNamesConstants()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var allToolNames = new HashSet<string>(
            definitions
                .Where(d => d.Tools is not null)
                .SelectMany(d => d.Tools!));

        var allDisallowedNames = new HashSet<string>(
            definitions
                .Where(d => d.DisallowedTools is not null)
                .SelectMany(d => d.DisallowedTools!));

        var allReferenced = allToolNames.Concat(allDisallowedNames);

        foreach (var toolName in allReferenced)
        {
            toolName.Should().NotBeNullOrEmpty($"tool name should not be empty or null");
            toolName.Should().NotContain(" ", $"tool name '{toolName}' should not contain spaces");
        }
    }

    [Fact]
    public void GetBuiltInDefinitions_ToolNamesMatchToolNamesConstants()
    {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var codeAgent = definitions.First(d => d.AgentType == "code");

        codeAgent.Tools.Should().Contain(FileToolNameConstants.FileRead, $"code agent should use FileToolNameConstants.FileRead ('{FileToolNameConstants.FileRead}')");
        codeAgent.Tools.Should().Contain(ShellToolNameConstants.Bash, $"code agent should use ShellToolNameConstants.Bash ('{ShellToolNameConstants.Bash}')");
        codeAgent.Tools.Should().Contain(SearchToolNameConstants.SearchCodebase, $"code agent should use SearchToolNameConstants.SearchCodebase ('{SearchToolNameConstants.SearchCodebase}')");
    }

    [Fact]
    public void ParseDefinitionFile_ValidFrontmatter_ParsesCorrectly()
    {
        var content = """
            ---
            when_to_use: "Custom agent for testing"
            tools:
              - Read
              - Glob
            disallowed_tools:
              - Write
            ---
            You are a custom test agent.
            """;

        var result = AgentDefinitionProvider.ParseDefinitionFile(content, "/agents/test.md");

        result.Should().NotBeNull();
        result!.AgentType.Should().Be("test");
        result.WhenToUse.Should().Be("Custom agent for testing");
        result.Tools.Should().Contain(["Read", "Glob"]);
        result.DisallowedTools.Should().Contain(["Write"]);
        result.SystemPrompt.Should().Be("You are a custom test agent.");
    }

    [Fact]
    public void ParseDefinitionFile_NoFrontmatter_UsesContentAsPrompt()
    {
        var content = "You are a simple agent without frontmatter.";

        var result = AgentDefinitionProvider.ParseDefinitionFile(content, "/agents/simple.md");

        result.Should().NotBeNull();
        result!.AgentType.Should().Be("simple");
        result.SystemPrompt.Should().Be("You are a simple agent without frontmatter.");
        result.Tools.Should().BeNull();
    }

    [Fact]
    public async Task GetAgentDefinitionsAsync_ReturnsBuiltInDefinitions()
    {
        var provider = new AgentDefinitionProvider(new IO.FileSystem.PhysicalFileSystem());

        var definitions = await provider.GetAgentDefinitionsAsync().ConfigureAwait(true);

        definitions.Should().NotBeEmpty();
        definitions.Select(d => d.AgentType).Should().Contain(["default", "code", "search", "Explore", "Plan"]);
    }

    [Fact]
    public async Task GetAgentDefinitionAsync_ReturnsCorrectAgentByType()
    {
        var provider = new AgentDefinitionProvider(new IO.FileSystem.PhysicalFileSystem());

        var codeAgent = await provider.GetAgentDefinitionAsync("code").ConfigureAwait(true);

        codeAgent.Should().NotBeNull();
        codeAgent!.AgentType.Should().Be("code");
        codeAgent.Tools.Should().Contain(FileToolNameConstants.FileRead);
    }

    [Fact]
    public async Task GetAgentDefinitionAsync_UnknownType_ReturnsNull()
    {
        var provider = new AgentDefinitionProvider(new IO.FileSystem.PhysicalFileSystem());

        var result = await provider.GetAgentDefinitionAsync("nonexistent").ConfigureAwait(true);

        result.Should().BeNull();
    }
}
