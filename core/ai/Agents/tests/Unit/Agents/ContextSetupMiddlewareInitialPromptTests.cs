namespace Core.Agents.Tests.Unit.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// ContextSetupMiddleware InitialPrompt 注入测试
/// 验证 AgentDefinition.InitialPrompt 正确传递到 SubAgentOptions.InitialPrompt
/// </summary>
public sealed class ContextSetupMiddlewareInitialPromptTests
{
    private static MiddlewareDelegate<UnifiedSpawnContext> NoopNext => (_, _) => Task.CompletedTask;

    [Fact]
    public async Task InvokeAsync_DefinitionHasInitialPrompt_SetsSubOptionsInitialPrompt()
    {
        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
        {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Code,
            WhenToUse = "code agent",
            InitialPrompt = "/review the code",
        };
        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        var mw = new ContextSetupMiddleware(contextAccessor.Object);

        var ctx = new UnifiedSpawnContext
        {
            Task = "test task",
            IsMainAgent = false,
            SpawnOptions = new AgentSpawnOptions
            {
                Description = "test",
                Prompt = "do something",
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
            },
            Definition = definition,
        };

        await mw.InvokeAsync(ctx, NoopNext, default);

        ctx.ResolvedSubOptions.Should().NotBeNull();
        ctx.ResolvedSubOptions!.InitialPrompt.Should().Be("/review the code");
    }

    [Fact]
    public async Task InvokeAsync_DefinitionWithoutInitialPrompt_SubOptionsInitialPromptIsNull()
    {
        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
        {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Code,
            WhenToUse = "code agent",
        };
        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        var mw = new ContextSetupMiddleware(contextAccessor.Object);

        var ctx = new UnifiedSpawnContext
        {
            Task = "test task",
            IsMainAgent = false,
            SpawnOptions = new AgentSpawnOptions
            {
                Description = "test",
                Prompt = "do something",
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
            },
            Definition = definition,
        };

        await mw.InvokeAsync(ctx, NoopNext, default);

        ctx.ResolvedSubOptions.Should().NotBeNull();
        ctx.ResolvedSubOptions!.InitialPrompt.Should().BeNull();
    }
}
