namespace Core.Agents.Tests.Unit.Agents;

/// <summary>
/// 统一 Spawn 管道主代理 no-op 单元测试
/// 验证 IsMainAgent=true 时各中间件正确跳过，防止递归和副作用
/// </summary>
public sealed class UnifiedSpawnMainAgentTests
{
    private static MiddlewareDelegate<UnifiedSpawnContext> NoopNext => (_, _) => Task.CompletedTask;

    private static UnifiedSpawnContext CreateMainAgentContext()
    {
        var agentMock = new Mock<IAgent>();
        agentMock.SetupGet(x => x.ObjectId).Returns(JoinCode.Abstractions.Entity.ObjectId.Empty);
        return new UnifiedSpawnContext
        {
            Task = "main task",
            IsMainAgent = true,
            Agent = agentMock.Object,
        };
    }

    [Fact]
    public async Task DefinitionResolution_IsMainAgent_SkipsGetProfile()
    {
        var roleRegistry = new Mock<IAgentRoleRegistry>();
        var mw = new DefinitionResolutionMiddleware(roleRegistry.Object);

        var ctx = CreateMainAgentContext();
        await mw.InvokeAsync(ctx, NoopNext, default);

        roleRegistry.Verify(x => x.GetProfile(It.IsAny<AgentRole>(), It.IsAny<JoinCode.Abstractions.Models.Agent.ExecutorVariant?>()), Times.Never);
        Assert.Null(ctx.Definition);
    }

    [Fact]
    public async Task PromptBuilding_IsMainAgent_SkipsBuildPrompt()
    {
        var promptBuilder = new Mock<IAgentPromptBuilder>();
        var mw = new PromptBuildingMiddleware(promptBuilder.Object);

        var ctx = CreateMainAgentContext();
        await mw.InvokeAsync(ctx, NoopNext, default);

        promptBuilder.Verify(x => x.BuildSystemPromptAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(string.Empty, ctx.SystemPrompt);
    }

    [Fact]
    public async Task LifecycleSpawn_AgentExists_SkipsSpawn()
    {
        var lifecycleManager = new Mock<IAgentLifecycleManager>();
        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        var mw = new LifecycleSpawnMiddleware(lifecycleManager.Object, contextAccessor.Object);

        var ctx = CreateMainAgentContext();
        var originalAgent = ctx.Agent;
        await mw.InvokeAsync(ctx, NoopNext, default);

        lifecycleManager.Verify(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Same(originalAgent, ctx.Agent);
    }

    [Fact]
    public async Task WorktreeSpawn_IsMainAgent_SkipsWorktree()
    {
        var worktreeService = new Mock<IAgentWorktreeService>();
        var worktreeManager = new Mock<IAgentWorktreeManager>();
        var mw = new WorktreeSpawnMiddleware(worktreeService.Object, worktreeManager.Object);

        var ctx = CreateMainAgentContext();
        await mw.InvokeAsync(ctx, NoopNext, default);

        worktreeService.Verify(x => x.CreateAgentWorktreeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<JoinCode.Abstractions.Models.WorktreeOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        worktreeManager.Verify(x => x.CreateWorktreeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(ctx.WorktreeCreated);
    }

    [Fact]
    public async Task TeammatePane_IsMainAgent_SkipsPane()
    {
        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        var layoutManager = new Mock<ITeammateLayoutManager>();
        var mw = new TeammatePaneMiddleware(contextAccessor.Object, NullLogger<TeammatePaneMiddleware>.Instance, layoutManager.Object);

        var ctx = CreateMainAgentContext();
        await mw.InvokeAsync(ctx, NoopNext, default);

        layoutManager.Verify(x => x.CreateTeammatePaneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(ctx.TeammatePaneCreated);
    }

    [Fact]
    public async Task Transcript_IsMainAgent_SkipsTranscript()
    {
        var clock = new Mock<JoinCode.Abstractions.Clock.IClockService>();
        var transcriptService = new Mock<IAgentTranscriptService>();
        // T10：构造新增 IChatContextManager 参数（子代理挂当前引擎会话）；主代理路径不触达
        var mw = new TranscriptMiddleware(clock.Object, contextManager: null, transcriptService: transcriptService.Object);

        var ctx = CreateMainAgentContext();
        await mw.InvokeAsync(ctx, NoopNext, default);

        transcriptService.Verify(x => x.AppendEntryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TranscriptEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterMessage_IsMainAgent_SkipsRegister()
    {
        var messageBroker = new Mock<IMailbox>();
        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        var mw = new RegisterMessageMiddleware(messageBroker.Object, contextAccessor.Object, NullLogger<RegisterMessageMiddleware>.Instance);

        var ctx = CreateMainAgentContext();
        await mw.InvokeAsync(ctx, NoopNext, default);

        messageBroker.Verify(x => x.RegisterAgent(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.False(ctx.MessageRegistered);
    }

    [Fact]
    public async Task ContextSetup_IsMainAgent_SkipsSubOptionsAssembly()
    {
        var contextAccessor = new Mock<ISubAgentContextAccessor>();
        var mw = new ContextSetupMiddleware(contextAccessor.Object);

        var ctx = CreateMainAgentContext();
        await mw.InvokeAsync(ctx, NoopNext, default);

        Assert.Null(ctx.ResolvedSubOptions);
        Assert.Null(ctx.CacheSafeParams);
    }
}
