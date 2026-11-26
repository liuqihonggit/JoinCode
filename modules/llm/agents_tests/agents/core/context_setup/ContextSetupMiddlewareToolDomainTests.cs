namespace Core.Agents.Tests.Unit.Agents;

/// <summary>
/// ContextSetupMiddleware 工具域合并测试 — 调用方只能收窄、不能扩大 AgentDefinition 的工具域。
/// </summary>
public sealed class ContextSetupMiddlewareToolDomainTests {
    private static MiddlewareDelegate<UnifiedSpawnContext> NoopNext => (_, _) => Task.CompletedTask;

    [Fact]
    public async Task InvokeAsync_CallerWithinDefinition_KeepsCallerNarrowing() {
        var options = await ResolveAsync(
            callerTools: ["Read", "Grep"],
            definitionTools: ["Read", "Write", "Grep"]);

        options.AllowedTools.Should().BeEquivalentTo(["Read", "Grep"]);
    }

    [Fact]
    public async Task InvokeAsync_CallerExceedsDefinition_IntersectsInsteadOfOverride() {
        var options = await ResolveAsync(
            callerTools: ["Read", "Bash"],
            definitionTools: ["Read", "Grep"]);

        options.AllowedTools.Should().BeEquivalentTo(["Read"]);
    }

    [Fact]
    public async Task InvokeAsync_CallerUnspecified_UsesDefinition() {
        var options = await ResolveAsync(
            callerTools: null,
            definitionTools: ["Read", "Grep"]);

        options.AllowedTools.Should().BeEquivalentTo(["Read", "Grep"]);
    }

    [Fact]
    public async Task InvokeAsync_DefinitionUnspecified_UsesCaller() {
        var options = await ResolveAsync(
            callerTools: ["Read"],
            definitionTools: []);

        options.AllowedTools.Should().BeEquivalentTo(["Read"]);
    }

    [Fact]
    public async Task InvokeAsync_DisjointDomains_DeniesAllInsteadOfUnrestricting() {
        var options = await ResolveAsync(
            callerTools: ["Bash"],
            definitionTools: ["Read", "Grep"]);

        options.AllowedTools.Should().ContainSingle()
            .Which.Should().Be(ToolDomainConstants.NoToolsPermitted);
    }

    [Fact]
    public async Task InvokeAsync_ToolNameCaseDiffers_StillMatchesDefinition() {
        var options = await ResolveAsync(
            callerTools: ["read"],
            definitionTools: ["Read", "Grep"]);

        options.AllowedTools.Should().ContainSingle()
            .Which.Should().Be("read");
    }

    private static async Task<SubAgentOptions> ResolveAsync(string[]? callerTools, List<string> definitionTools) {
        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Code,
            WhenToUse = "code agent",
            Tools = definitionTools,
        };
        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        await using var middleware = new ContextSetupMiddleware(contextAccessor.Object);

        var context = new UnifiedSpawnContext {
            Task = "test task",
            IsMainAgent = false,
            SpawnOptions = new AgentSpawnOptions {
                Description = "test",
                Prompt = "do something",
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                AllowedTools = callerTools,
            },
            Definition = definition,
        };

        await middleware.InvokeAsync(context, NoopNext, default);

        Assert.NotNull(context.ResolvedSubOptions);
        return context.ResolvedSubOptions;
    }
}
