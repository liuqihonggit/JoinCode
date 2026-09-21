namespace Sync.Tests.Agents;

public sealed class AgentDefinitionProviderTests {
    [Fact]
    public void GetBuiltInDefinitions_ReturnsNineAgentTypes() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();

        definitions.Should().HaveCount(9);
        definitions.Select(d => d.DisplayId).Should().Contain(["coordinator", "executor:code", "executor:search", "executor:explore", "executor:plan", "executor:doctor", "executor:verification", "executor:claudeCodeGuide", "executor:contextCompression"]);
    }

    [Fact]
    public void GetBuiltInDefinitions_CoordinatorAgent_HasSubAgentDisallowedTools() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var coordinator = definitions.First(d => d.Role == AgentRole.Coordinator);

        coordinator.Tools.Should().BeEmpty();
        coordinator.DisallowedTools.Should().NotBeNull();
        coordinator.DisallowedTools.Should().Contain([
            AgentToolNameEnumConstants.Agent, AgentToolNameEnumConstants.AgentSpawn
        ]);
    }

    [Fact]
    public void GetBuiltInDefinitions_CodeAgent_UsesToolNamesConstants() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var codeAgent = definitions.First(d => d.Variant == ExecutorVariant.Code);

        codeAgent.Tools.Should().NotBeNull();
        codeAgent.Tools.Should().Contain([
            FileToolNameEnumConstants.FileRead,
            FileToolNameEnumConstants.FileWrite,
            FileToolNameEnumConstants.FileEdit,
            SearchToolNameEnumConstants.Glob,
            SearchToolNameEnumConstants.Grep,
            ShellToolNameEnumConstants.Bash,
            SearchToolNameEnumConstants.SearchCodebase
        ]);
        codeAgent.DisallowedTools.Should().NotBeNull();
        codeAgent.DisallowedTools.Should().Contain([
            AgentToolNameEnumConstants.Agent, AgentToolNameEnumConstants.AgentSpawn
        ]);
    }

    [Fact]
    public void GetBuiltInDefinitions_SearchAgent_UsesToolNamesConstants() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var searchAgent = definitions.First(d => d.Variant == ExecutorVariant.Search);

        searchAgent.Tools.Should().NotBeNull();
        searchAgent.Tools.Should().Contain([
            FileToolNameEnumConstants.FileRead,
            SearchToolNameEnumConstants.Glob,
            SearchToolNameEnumConstants.Grep,
            SearchToolNameEnumConstants.SearchCodebase
        ]);

        searchAgent.DisallowedTools.Should().NotBeNull();
        searchAgent.DisallowedTools.Should().Contain([
            FileToolNameEnumConstants.FileWrite,
            FileToolNameEnumConstants.FileEdit,
            ShellToolNameEnumConstants.Bash
        ]);
    }

    [Fact]
    public void GetBuiltInDefinitions_CodeAgent_DisallowedToolsContainsSubAgentTools() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var codeAgent = definitions.First(d => d.Variant == ExecutorVariant.Code);

        codeAgent.DisallowedTools.Should().NotBeNull();
        codeAgent.DisallowedTools.Should().Contain([
            AgentToolNameEnumConstants.Agent, AgentToolNameEnumConstants.AgentSpawn
        ]);
    }

    [Fact]
    public void GetBuiltInDefinitions_SearchAgent_DisallowedWriteTools() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var searchAgent = definitions.First(d => d.Variant == ExecutorVariant.Search);

        searchAgent.DisallowedTools.Should().NotBeNullOrEmpty();
        searchAgent.DisallowedTools.Should().Contain(FileToolNameEnumConstants.FileWrite);
        searchAgent.DisallowedTools.Should().Contain(FileToolNameEnumConstants.FileEdit);
        searchAgent.DisallowedTools.Should().Contain(ShellToolNameEnumConstants.Bash);
    }

    [Fact]
    public void GetBuiltInDefinitions_ExploreAgent_IsReadOnly() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var exploreAgent = definitions.First(d => d.Variant == ExecutorVariant.Explore);

        exploreAgent.Tools.Should().NotBeNull();
        exploreAgent.Tools.Should().Contain([FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase, ShellToolNameEnumConstants.Bash]);
        exploreAgent.DisallowedTools.Should().NotBeNull();
        exploreAgent.DisallowedTools.Should().Contain([AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite]);
        exploreAgent.IsBackground.Should().BeFalse();
    }

    [Fact]
    public void GetBuiltInDefinitions_PlanAgent_IsReadOnly() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var planAgent = definitions.First(d => d.Variant == ExecutorVariant.Plan);

        planAgent.Tools.Should().NotBeNull();
        planAgent.Tools.Should().Contain([FileToolNameEnumConstants.FileRead, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, SearchToolNameEnumConstants.SearchCodebase, ShellToolNameEnumConstants.Bash]);
        planAgent.DisallowedTools.Should().NotBeNull();
        planAgent.DisallowedTools.Should().Contain([AgentToolNameEnumConstants.Agent, FileToolNameEnumConstants.FileEdit, FileToolNameEnumConstants.FileWrite]);
        planAgent.IsBackground.Should().BeFalse();
    }

    [Fact]
    public void GetBuiltInDefinitions_DoctorAgent_IsBackgroundWithDoctorPermission() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var doctorAgent = definitions.First(d => d.Variant == ExecutorVariant.Doctor);

        doctorAgent.IsBackground.Should().BeTrue();
        doctorAgent.PermissionMode.Should().Be("doctor");
        doctorAgent.Tools.Should().NotBeNull();
        doctorAgent.Tools.Should().Contain([FileToolNameEnumConstants.FileRead, FileToolNameEnumConstants.FileEdit, SearchToolNameEnumConstants.Glob, SearchToolNameEnumConstants.Grep, ShellToolNameEnumConstants.Bash]);
        doctorAgent.DisallowedTools.Should().Contain(AgentToolNameEnumConstants.Agent);
    }

    [Fact]
    public void GetBuiltInDefinitions_AllToolNames_AreValidToolNamesConstants() {
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

        foreach (var toolName in allReferenced) {
            toolName.Should().NotBeNullOrEmpty($"tool name should not be empty or null");
            toolName.Should().NotContain(" ", $"tool name '{toolName}' should not contain spaces");
        }
    }

    [Fact]
    public void GetBuiltInDefinitions_ToolNamesMatchToolNamesConstants() {
        var definitions = AgentDefinitionProvider.GetBuiltInDefinitions();
        var codeAgent = definitions.First(d => d.Variant == ExecutorVariant.Code);

        codeAgent.Tools.Should().Contain(FileToolNameEnumConstants.FileRead, $"code agent should use FileToolNameEnumConstants.FileRead ('{FileToolNameEnumConstants.FileRead}')");
        codeAgent.Tools.Should().Contain(ShellToolNameEnumConstants.Bash, $"code agent should use ShellToolNameEnumConstants.Bash ('{ShellToolNameEnumConstants.Bash}')");
        codeAgent.Tools.Should().Contain(SearchToolNameEnumConstants.SearchCodebase, $"code agent should use SearchToolNameEnumConstants.SearchCodebase ('{SearchToolNameEnumConstants.SearchCodebase}')");
    }

    [Fact]
    public void ParseDefinitionFile_ValidFrontmatter_ParsesCorrectly() {
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
        result!.Role.Should().Be(AgentRole.Executor);
        result.Variant.Should().BeNull();
        result.DisplayId.Should().Be("executor");
        result.WhenToUse.Should().Be("Custom agent for testing");
        result.Tools.Should().Contain(["Read", "Glob"]);
        result.DisallowedTools.Should().Contain(["Write"]);
        result.SystemPrompt.Should().Be("You are a custom test agent.");
    }

    [Fact]
    public void ParseDefinitionFile_NoFrontmatter_UsesContentAsPrompt() {
        var content = "You are a simple agent without frontmatter.";

        var result = AgentDefinitionProvider.ParseDefinitionFile(content, "/agents/simple.md");

        result.Should().NotBeNull();
        result!.Role.Should().Be(AgentRole.Executor);
        result.Variant.Should().BeNull();
        result.DisplayId.Should().Be("executor");
        result.SystemPrompt.Should().Be("You are a simple agent without frontmatter.");
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAgentDefinitionsAsync_ReturnsBuiltInDefinitions() {
        await using var provider = new AgentDefinitionProvider(new IO.FileSystem.PhysicalFileSystem());

        var definitions = await provider.GetAgentDefinitionsAsync().ConfigureAwait(true);

        definitions.Should().NotBeEmpty();
        definitions.Select(d => d.DisplayId).Should().Contain(["coordinator", "executor:code", "executor:search", "executor:explore", "executor:plan", "executor:doctor"]);
    }

    [Fact]
    public async Task GetAgentDefinitionAsync_ReturnsCorrectAgentByType() {
        await using var provider = new AgentDefinitionProvider(new IO.FileSystem.PhysicalFileSystem());

        var codeAgent = await provider.GetAgentDefinitionAsync(AgentRole.Executor, ExecutorVariant.Code).ConfigureAwait(true);

        codeAgent.Should().NotBeNull();
        codeAgent!.DisplayId.Should().Be("executor:code");
        codeAgent.Tools.Should().Contain(FileToolNameEnumConstants.FileRead);
    }

    [Fact]
    public async Task GetAgentDefinitionAsync_UnknownType_ReturnsNull() {
        await using var provider = new AgentDefinitionProvider(new IO.FileSystem.PhysicalFileSystem());

        var result = await provider.GetAgentDefinitionAsync(AgentRole.Executor, (ExecutorVariant)999).ConfigureAwait(true);

        result.Should().BeNull();
    }
}