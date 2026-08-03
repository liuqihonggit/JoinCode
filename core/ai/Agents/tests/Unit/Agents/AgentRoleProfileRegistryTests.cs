namespace Core.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;
using Moq;

public sealed class AgentRoleProfileRegistryTests
{
    [Fact]
    public void BuildBuiltInProfiles_ReturnsSixProfiles()
    {
        var profiles = AgentRoleProfileRegistry.BuildBuiltInProfiles();

        profiles.Should().HaveCount(6);
        profiles.Count(p => p.Role == AgentRole.Coordinator).Should().Be(1);
        profiles.Count(p => p.Role == AgentRole.Executor).Should().Be(5);
    }

    [Fact]
    public void GetProfile_Coordinator_ReturnsCoordinatorProfile()
    {
        var registry = new AgentRoleProfileRegistry();

        var profile = registry.GetProfile(AgentRole.Coordinator);

        profile.Should().NotBeNull();
        profile!.Role.Should().Be(AgentRole.Coordinator);
        profile.Variant.Should().BeNull();
        profile.AllowedTools.Should().BeNull();
    }

    [Fact]
    public void GetProfile_ExecutorCode_ReturnsCodeProfile()
    {
        var registry = new AgentRoleProfileRegistry();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.Code);

        profile.Should().NotBeNull();
        profile!.Role.Should().Be(AgentRole.Executor);
        profile.Variant.Should().Be(ExecutorVariant.Code);
        profile.AllowedTools.Should().NotBeNull();
        profile.AllowedTools.Should().Contain(FileToolNameConstants.FileRead);
    }

    [Fact]
    public void GetProfile_ExecutorExplore_IsOneShot()
    {
        var registry = new AgentRoleProfileRegistry();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.Explore);

        profile.Should().NotBeNull();
        profile!.IsOneShot.Should().BeTrue();
        profile.OmitClaudeMd.Should().BeTrue();
        profile.OmitGitStatus.Should().BeTrue();
    }

    [Fact]
    public void GetProfile_ExecutorDoctor_IsBackground()
    {
        var registry = new AgentRoleProfileRegistry();

        var profile = registry.GetProfile(AgentRole.Executor, ExecutorVariant.Doctor);

        profile.Should().NotBeNull();
        profile!.IsBackground.Should().BeTrue();
        profile.PermissionMode.Should().Be("doctor");
    }

    [Fact]
    public void GetProfile_UnknownVariant_ReturnsNull()
    {
        var registry = new AgentRoleProfileRegistry();

        var profile = registry.GetProfile(AgentRole.Executor, (ExecutorVariant)999);

        profile.Should().BeNull();
    }

    [Fact]
    public void GetAvailableVariants_ReturnsFiveVariants()
    {
        var registry = new AgentRoleProfileRegistry();

        var variants = registry.GetAvailableVariants();

        variants.Should().HaveCount(5);
        variants.Should().Contain([
            ExecutorVariant.Code, ExecutorVariant.Search,
            ExecutorVariant.Explore, ExecutorVariant.Plan, ExecutorVariant.Doctor
        ]);
    }

    [Fact]
    public void Register_AddsCustomProfile()
    {
        var registry = new AgentRoleProfileRegistry();

        var custom = new AgentRoleProfile
        {
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
    public void ClearCache_ResetsToBuiltInProfiles()
    {
        var registry = new AgentRoleProfileRegistry();

        var custom = new AgentRoleProfile
        {
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
    public void GetProfilesByRole_ReturnsOnlyExecutorProfiles()
    {
        var registry = new AgentRoleProfileRegistry();

        var executorProfiles = registry.GetProfilesByRole(AgentRole.Executor);

        executorProfiles.Should().HaveCount(5);
        executorProfiles.All(p => p.Role == AgentRole.Executor).Should().BeTrue();
    }
}
