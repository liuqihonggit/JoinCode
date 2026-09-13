namespace Core.Agents.Tests.Unit.Agents;


/// <summary>
/// ContextSetupMiddleware 子代理模型解析测试
/// 验证优先级链: JCC_SUBAGENT_MODEL > SpawnOptions.Model > Definition.ModelName > inherit/父级模型
/// 对齐 TS 原版 getAgentModel 设计
/// </summary>
public sealed class ContextSetupMiddlewareSubagentModelTests
{
    private static MiddlewareDelegate<UnifiedSpawnContext> NoopNext => (_, _) => Task.CompletedTask;

    #region 环境变量优先级

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

    #endregion

    #region inherit 关键字

    [Theory]
    [InlineData("inherit")]
    [InlineData("Inherit")]
    [InlineData("INHERIT")]
    public async Task InvokeAsync_SpawnModelIsInherit_ResolvesToParentModel(string inheritKeyword)
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware(spawnModel: inheritKeyword, model: "definition-model", parentModel: "parent-opus-4-6");
        ctx.ResolvedSubOptions!.ModelName.Should().Be("parent-opus-4-6");
    }

    [Theory]
    [InlineData("inherit")]
    [InlineData("Inherit")]
    public async Task InvokeAsync_DefinitionModelIsInherit_ResolvesToParentModel(string inheritKeyword)
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware(model: inheritKeyword, parentModel: "parent-opus-4-6");
        ctx.ResolvedSubOptions!.ModelName.Should().Be("parent-opus-4-6");
    }

    [Fact]
    public async Task InvokeAsync_BothNullAndParentModelSet_ResolvesToParentModel()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware(parentModel: "parent-opus-4-6");
        ctx.ResolvedSubOptions!.ModelName.Should().Be("parent-opus-4-6");
    }

    [Fact]
    public async Task InvokeAsync_BothNullAndNoParentModel_ResolvesToNull()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware();
        ctx.ResolvedSubOptions!.ModelName.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_InheritWithNoParentModel_ResolvesToNull()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware(spawnModel: "inherit");
        ctx.ResolvedSubOptions!.ModelName.Should().BeNull();
    }

    #endregion

    #region alias 匹配父 tier

    [Fact]
    public async Task InvokeAsync_SpawnModelAliasMatchesParentTier_ResolvesToParentModel()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware(spawnModel: "opus", parentModel: "claude-opus-4-6");
        ctx.ResolvedSubOptions!.ModelName.Should().Be("claude-opus-4-6");
    }

    [Fact]
    public async Task InvokeAsync_SpawnModelAliasDoesNotMatchParentTier_ResolvesToSpawnModel()
    {
        Environment.SetEnvironmentVariable("JCC_SUBAGENT_MODEL", null);
        var ctx = await InvokeMiddleware(spawnModel: "opus", parentModel: "claude-sonnet-4-6");
        ctx.ResolvedSubOptions!.ModelName.Should().Be("opus");
    }

    #endregion

    private static async Task<UnifiedSpawnContext> InvokeMiddleware(
        string? spawnModel = null, string? model = null, string? parentModel = null)
    {
        var definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
        {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Code,
            WhenToUse = "code agent",
            ModelName = model,
        };

        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        if (parentModel is not null)
        {
            var parentContext = new SubAgentContext
            {
                AgentId = "parent-agent",
                Role = AgentRole.Executor,
                Task = "parent task",
                CacheSafeParams = new CacheSafeParams { ModelId = parentModel },
            };
            contextAccessor.Setup(x => x.Current).Returns(parentContext);
        }

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
