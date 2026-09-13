namespace Core.Agents.Tests.Unit.Agents;


/// <summary>
/// ContextSetupMiddleware Skills 预加载测试
/// 验证 PreloadSkills 字段正确消费: 加载 skill 内容到 InitialMessageList
/// </summary>
public sealed class ContextSetupMiddlewareSkillPreloadTests
{
    private static MiddlewareDelegate<UnifiedSpawnContext> NoopNext => (_, _) => Task.CompletedTask;

    [Fact]
    public async Task InvokeAsync_DefinitionHasSkills_LoadsSkillContentToInitialMessageList()
    {
        var skill = new SkillDefinition
        {
            Name = "commit",
            Description = "Git commit skill",
            Steps = [new SkillStep { Id = "execute", Type = SkillStepType.Prompt, Prompt = "Run git commit with message" }],
        };
        var skillServiceMock = new Mock<ISkillService>();
        skillServiceMock.Setup(x => x.GetSkillAsync("commit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(skill);

        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        var mw = new ContextSetupMiddleware(contextAccessor.Object, null, skillServiceMock.Object);

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
            Definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                WhenToUse = "code agent",
                Skills = ["commit"],
            },
        };

        await mw.InvokeAsync(ctx, NoopNext, default);

        ctx.ResolvedSubOptions.Should().NotBeNull();
        ctx.ResolvedSubOptions!.InitialMessageList.Should().NotBeNull();
        ctx.ResolvedSubOptions!.InitialMessageList!.Count.Should().BeGreaterThan(0);
        ctx.ResolvedSubOptions!.PreloadSkills.Should().Contain("commit");
    }

    [Fact]
    public async Task InvokeAsync_NoSkillService_InitialMessageListIsNull()
    {
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
            Definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                WhenToUse = "code agent",
                Skills = ["commit"],
            },
        };

        await mw.InvokeAsync(ctx, NoopNext, default);

        ctx.ResolvedSubOptions.Should().NotBeNull();
        ctx.ResolvedSubOptions!.InitialMessageList.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_SkillNotFound_InitialMessageListIsEmpty()
    {
        var skillServiceMock = new Mock<ISkillService>();
        skillServiceMock.Setup(x => x.GetSkillAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SkillDefinition?)null);

        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        var mw = new ContextSetupMiddleware(contextAccessor.Object, null, skillServiceMock.Object);

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
            Definition = new JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                WhenToUse = "code agent",
                Skills = ["nonexistent"],
            },
        };

        await mw.InvokeAsync(ctx, NoopNext, default);

        ctx.ResolvedSubOptions.Should().NotBeNull();
        ctx.ResolvedSubOptions!.InitialMessageList.Should().NotBeNull();
        ctx.ResolvedSubOptions!.InitialMessageList!.Count.Should().Be(0);
    }
}
