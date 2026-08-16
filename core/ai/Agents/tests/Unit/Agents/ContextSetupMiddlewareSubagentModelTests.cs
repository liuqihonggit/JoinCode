namespace Core.Agents.Tests.Unit.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// ContextSetupMiddleware JCC_SUBAGENT_MODEL 环境变量覆盖测试
/// 验证环境变量优先级: JCC_SUBAGENT_MODEL > SpawnOptions.Model > Definition.ModelName
/// </summary>
public sealed class ContextSetupMiddlewareSubagentModelTests
{
    private static MiddlewareDelegate<UnifiedSpawnContext> NoopNext => (_, _) => Task.CompletedTask;

    [Fact]
    public async Task InvokeAsync_EnvSubagentModelSet_OverridesAll()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", "gpt-4o-mini");
        try
        {
            var ctx = await InvokeMiddleware(model: "definition-model");
            ctx.ResolvedSubOptions!.ModelName.Should().Be("gpt-4o-mini");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        }
    }

    [Fact]
    public async Task InvokeAsync_EnvSubagentModelUnset_FallsBackToSpawnOptions()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware(spawnModel: "spawn-model", model: "definition-model");
        ctx.ResolvedSubOptions!.ModelName.Should().Be("spawn-model");
    }

    [Fact]
    public async Task InvokeAsync_EnvAndSpawnUnset_FallsBackToDefinition()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware(model: "definition-model");
        ctx.ResolvedSubOptions!.ModelName.Should().Be("definition-model");
    }

    private static async Task<UnifiedSpawnContext> InvokeMiddleware(
        string? spawnModel = null, string? model = null)
    {
        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
        {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Code,
            WhenToUse = "code agent",
            ModelName = model,
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
                Model = spawnModel,
            },
            Definition = definition,
        };

        await mw.InvokeAsync(ctx, NoopNext, default);
        return ctx;
    }
}
