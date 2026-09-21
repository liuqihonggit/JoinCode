namespace Core.Agents;


public sealed class AgentRoleProfileRegistryTests {
    [Fact]
    public void BuildBuiltInProfiles_ReturnsNineProfiles() {
        var profiles = AgentRoleProfileRegistry.BuildBuiltInProfiles();

        profiles.Should().HaveCount(9);
        profiles.Count(p => p.Role == AgentRole.Coordinator).Should().Be(1);
        profiles.Count(p => p.Role == AgentRole.Executor).Should().Be(8);
    }

    [Fact]
    public async Task GetProfile_Coordinator_ReturnsCoordinatorProfile() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Coordinator);

        profile.Should().NotBeNull();
        profile!.Role.Should().Be(AgentRole.Coordinator);
        profile.Variant.Should().BeNull();
        profile.AllowedTools.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProfile_ExecutorCode_ReturnsCodeProfile() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.Code);

        profile.Should().NotBeNull();
        profile!.Role.Should().Be(AgentRole.Executor);
        profile.Variant.Should().Be(ExecutorVariant.Code);
        profile.AllowedTools.Should().NotBeNull();
        profile.AllowedTools.Should().Contain(FileToolNameEnumConstants.FileRead);
    }

    [Fact]
    public async Task GetProfile_ExecutorExplore_IsOneShot() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.Explore);

        profile.Should().NotBeNull();
        profile!.IsOneShot.Should().BeTrue();
        profile.OmitProjectRules.Should().BeTrue();
        profile.OmitGitStatus.Should().BeTrue();
    }

    [Fact]
    public async Task GetProfile_ExecutorDoctor_IsBackground() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.Doctor);

        profile.Should().NotBeNull();
        profile!.IsBackground.Should().BeTrue();
        profile.PermissionMode.Should().Be("doctor");
    }

    [Fact]
    public async Task GetProfile_UnknownVariant_ReturnsNull() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Executor, (ExecutorVariant)999);

        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetProfile_CustomDefinitionWithSourcePath_OverridesBuiltIn() {
        var customDef = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Code,
            WhenToUse = "custom code agent",
            Description = "Custom override",
            SourcePath = "/custom/agents/code.md",
        };
        var providerMock = new Mock<IAgentDefinitionProvider>();
        providerMock
            .Setup(x => x.GetAgentDefinitionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([customDef]);
        await using var registry = new AgentRoleProfileRegistry(providerMock.Object);
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.Code);

        profile.Should().NotBeNull();
        profile!.Description.Should().Be("Custom override");
    }

    [Fact]
    public async Task GetAvailableVariants_ReturnsEightVariants() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var variants = registry.GetAvailableVariants();

        variants.Should().HaveCount(8);
        variants.Should().Contain([
            ExecutorVariant.Code, ExecutorVariant.Search,
            ExecutorVariant.Explore, ExecutorVariant.Plan, ExecutorVariant.Doctor,
            ExecutorVariant.Verification, ExecutorVariant.JoinCodeGuide, ExecutorVariant.ContextCompression
        ]);
    }

    [Fact]
    public async Task Register_AddsCustomProfile() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var custom = new AgentRoleProfile {
            Role = AgentRole.Executor,
            Variant = (ExecutorVariant)100,
            WhenToUse = "Custom agent",
        };
        registry.Register(custom);

        var profile = registry.GetProfile(AgentRole.Executor, (ExecutorVariant)100);
        profile.Should().NotBeNull();
        profile!.WhenToUse.Should().Be("Custom agent");
    }

    [Fact]
    public async Task ClearCache_ResetsToBuiltInProfiles() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var custom = new AgentRoleProfile {
            Role = AgentRole.Executor,
            Variant = (ExecutorVariant)100,
            WhenToUse = "Custom agent",
        };
        registry.Register(custom);

        registry.GetProfile(AgentRole.Executor, (ExecutorVariant)100).Should().NotBeNull();

        registry.ClearCache();

        registry.GetProfile(AgentRole.Executor, (ExecutorVariant)100).Should().BeNull();
    }

    [Fact]
    public async Task GetProfilesByRole_ReturnsOnlyExecutorProfiles() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var executorProfiles = registry.GetProfilesByRole(AgentRole.Executor);

        executorProfiles.Should().HaveCount(8);
        executorProfiles.All(p => p.Role == AgentRole.Executor).Should().BeTrue();
    }

    [Fact]
    public async Task GetProfile_ExecutorVerification_HasCorrectTools() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.Verification);

        profile.Should().NotBeNull();
        profile!.AllowedTools.Should().Contain(FileToolNameEnumConstants.FileRead);
        profile.DisallowedTools.Should().Contain(AgentToolNameEnumConstants.Agent);
        profile.SystemPrompt.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetProfile_ExecutorJoinCodeGuide_HasCorrectTools() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.JoinCodeGuide);

        profile.Should().NotBeNull();
        profile!.AllowedTools.Should().Contain(FileToolNameEnumConstants.FileRead);
        profile.DisallowedTools.Should().Contain(ShellToolNameEnumConstants.Bash);
        profile.SystemPrompt.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetProfile_ExecutorContextCompression_HasCorrectTools() {
        await using var registry = new AgentRoleProfileRegistry();
        registry.RegisterBuiltInProfiles();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.ContextCompression);

        profile.Should().NotBeNull();
        profile!.AllowedTools.Should().Contain(FileToolNameEnumConstants.FileRead);
        profile.DisallowedTools.Should().Contain(FileToolNameEnumConstants.FileEdit);
        profile.SystemPrompt.Should().NotBeNullOrEmpty();
    }
}