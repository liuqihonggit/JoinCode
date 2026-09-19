namespace Sync.Tests.Agents.Coordinator.Pool;

/// <summary>
/// PreemptiveScheduler 单元测试 — 验证抢塞新任务的窗口检查+压缩逻辑（ADR 0106 L3）
/// </summary>
public sealed class PreemptiveSchedulerTests {
    private static AgentBase CreateAgent(string task = "test task", int? tokenBudget = 128000, int tokensUsed = 0) {
        var queryEngineMock = new Mock<IQueryEngine>();
        var agent = new AgentBase(task, null, queryEngineMock.Object, null, tokenBudget: tokenBudget);
        agent.Output.TokensUsed = tokensUsed;
        agent.Status = TaskExecutionStatus.Completed;
        return agent;
    }

    private static SubAgentLivenessOptions DefaultOptions() => new() {
        PoolMaxSize = 4,
        PreemptMinWindowRatio = 0.2,
    };

    [Fact]
    public async Task TryPreemptAsync_NoAvailableAgent_ReturnsNoAvailableAgent() {
        var pool = new SubAgentPool(DefaultOptions());
        var ctxMock = new Mock<IChatContextManager>();
        var scheduler = new PreemptiveScheduler(pool, ctxMock.Object, DefaultOptions());

        var result = await scheduler.TryPreemptAsync("any task");

        result.Success.Should().BeFalse();
        result.Agent.Should().BeNull();
        result.Reason.Should().Contain("无可用");
    }

    [Fact]
    public async Task TryPreemptAsync_WindowSufficient_SucceedsWithoutCompression() {
        var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("fix bug in parser", tokenBudget: 128000, tokensUsed: 1000);
        pool.Return(agent);
        var ctxMock = new Mock<IChatContextManager>();
        var scheduler = new PreemptiveScheduler(pool, ctxMock.Object, DefaultOptions());

        var result = await scheduler.TryPreemptAsync("fix bug in parser");

        result.Success.Should().BeTrue();
        result.Agent.Should().NotBeNull();
        result.ContextCompressed.Should().BeFalse();
        agent.Status.Should().Be(TaskExecutionStatus.Running);
    }

    [Fact]
    public async Task TryPreemptAsync_WindowInsufficient_CompressesThenSucceeds() {
        var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("fix bug in parser", tokenBudget: 1000, tokensUsed: 900);
        pool.Return(agent);
        var ctxMock = new Mock<IChatContextManager>();
        ctxMock
            .Setup(x => x.FoldIfNeededAsync(It.IsAny<ContextFoldDecision>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextFoldResult { Folded = true, Decision = ContextFoldDecision.FoldNormal });
        var scheduler = new PreemptiveScheduler(pool, ctxMock.Object, DefaultOptions());

        var result = await scheduler.TryPreemptAsync("fix bug in parser");

        result.Success.Should().BeTrue();
        result.ContextCompressed.Should().BeTrue();
        ctxMock.Verify(x => x.FoldIfNeededAsync(ContextFoldDecision.FoldNormal, agent.ObjectId.UniqueId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryPreemptAsync_WindowInsufficient_FoldNotExecuted_StillSucceeds() {
        var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("fix bug in parser", tokenBudget: 1000, tokensUsed: 900);
        pool.Return(agent);
        var ctxMock = new Mock<IChatContextManager>();
        ctxMock
            .Setup(x => x.FoldIfNeededAsync(It.IsAny<ContextFoldDecision>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextFoldResult { Folded = false });
        var scheduler = new PreemptiveScheduler(pool, ctxMock.Object, DefaultOptions());

        var result = await scheduler.TryPreemptAsync("fix bug in parser");

        result.Success.Should().BeTrue();
        result.ContextCompressed.Should().BeFalse();
    }

    [Fact]
    public async Task TryPreemptAsync_InjectsNewTaskPrompt() {
        var pool = new SubAgentPool(DefaultOptions());
        var agent = CreateAgent("fix bug in parser");
        pool.Return(agent);
        var ctxMock = new Mock<IChatContextManager>();
        var scheduler = new PreemptiveScheduler(pool, ctxMock.Object, DefaultOptions());

        await scheduler.TryPreemptAsync("fix bug in parser");

        agent.ChatHistory.Should().Contain(m => m.Content!.Contains("[新任务]"));
    }
}