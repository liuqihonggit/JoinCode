namespace Hands.ToolHandlers.Tests;

/// <summary>
/// AgentForkMiddleware Teammate 隔离模式决策单元测试
/// 缺陷修复验证(TASK002): Teammate 未显式传 isolation 时应通过 WorktreeDecisionPolicy 决策
/// </summary>
public sealed class AgentForkTeammateIsolationTests {
    [Fact]
    public async Task Teammate_NoExplicitIsolation_UsesWorktreeDecisionPolicy() {
        InProcessTeammateDefinition? capturedDefinition = null;
        var teammateExecutor = CreateTeammateExecutor(d => capturedDefinition = d);
        var worktreeManager = CreateWorktreeManager(isolationEnabled: true);
        var sut = new AgentForkMiddleware(
            CreateContextAccessor(),
            teammateExecutor: teammateExecutor,
            worktreeDecisionPolicy: new WorktreeDecisionPolicy(),
            worktreeManager: worktreeManager);

        var context = CreateAgentToolContext(isolation: null);
        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        capturedDefinition.Should().NotBeNull();
        capturedDefinition!.IsolationMode.Should().Be(AgentIsolationMode.Worktree,
            "未显式传 isolation 时应通过 WorktreeDecisionPolicy.Decide(true, Teammate) 决策为 Worktree");
    }

    [Fact]
    public async Task Teammate_ExplicitWorktree_Isolation_OverridesDecisionPolicy() {
        InProcessTeammateDefinition? capturedDefinition = null;
        var teammateExecutor = CreateTeammateExecutor(d => capturedDefinition = d);
        var sut = new AgentForkMiddleware(
            CreateContextAccessor(),
            teammateExecutor: teammateExecutor,
            worktreeDecisionPolicy: new WorktreeDecisionPolicy(),
            worktreeManager: CreateWorktreeManager(isolationEnabled: false));

        var context = CreateAgentToolContext(isolation: "worktree");
        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        capturedDefinition.Should().NotBeNull();
        capturedDefinition!.IsolationMode.Should().Be(AgentIsolationMode.Worktree,
            "显式传 isolation=worktree 应优先于决策策略");
    }

    [Fact]
    public async Task Teammate_ExplicitNone_Isolation_OverridesDecisionPolicy() {
        InProcessTeammateDefinition? capturedDefinition = null;
        var teammateExecutor = CreateTeammateExecutor(d => capturedDefinition = d);
        var sut = new AgentForkMiddleware(
            CreateContextAccessor(),
            teammateExecutor: teammateExecutor,
            worktreeDecisionPolicy: new WorktreeDecisionPolicy(),
            worktreeManager: CreateWorktreeManager(isolationEnabled: true));

        var context = CreateAgentToolContext(isolation: "none");
        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        capturedDefinition.Should().NotBeNull();
        capturedDefinition!.IsolationMode.Should().Be(AgentIsolationMode.None,
            "显式传 isolation=none 应优先于决策策略");
    }

    [Fact]
    public async Task Teammate_NoDecisionPolicy_FallsBackToNone() {
        InProcessTeammateDefinition? capturedDefinition = null;
        var teammateExecutor = CreateTeammateExecutor(d => capturedDefinition = d);
        var sut = new AgentForkMiddleware(
            CreateContextAccessor(),
            teammateExecutor: teammateExecutor);

        var context = CreateAgentToolContext(isolation: null);
        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        capturedDefinition.Should().NotBeNull();
        capturedDefinition!.IsolationMode.Should().Be(AgentIsolationMode.None,
            "无 WorktreeDecisionPolicy 时应 fallback 到 None");
    }

    [Fact]
    public async Task Teammate_WorktreeDisabled_GlobalSwitch_ReturnsNone() {
        InProcessTeammateDefinition? capturedDefinition = null;
        var teammateExecutor = CreateTeammateExecutor(d => capturedDefinition = d);
        var sut = new AgentForkMiddleware(
            CreateContextAccessor(),
            teammateExecutor: teammateExecutor,
            worktreeDecisionPolicy: new WorktreeDecisionPolicy(),
            worktreeManager: CreateWorktreeManager(isolationEnabled: false));

        var context = CreateAgentToolContext(isolation: null);
        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        capturedDefinition.Should().NotBeNull();
        capturedDefinition!.IsolationMode.Should().Be(AgentIsolationMode.None,
            "全局开关关闭时 Decide(false, Teammate) 应返回 None");
    }

    private static AgentToolContext CreateAgentToolContext(string? isolation)
        => new() {
            Description = "test",
            Prompt = "test task",
            SubagentType = null,
            Isolation = isolation,
        };

    private static ISubAgentContextAccessor CreateContextAccessor() {
        var mock = new Mock<ISubAgentContextAccessor>();
        mock.SetupGet(x => x.Current).Returns((SubAgentContext?)null);
        return mock.Object;
    }

    private static IAgentWorktreeManager CreateWorktreeManager(bool isolationEnabled) {
        var mock = new Mock<IAgentWorktreeManager>();
        mock.SetupGet(x => x.IsWorktreeIsolationEnabled).Returns(isolationEnabled);
        return mock.Object;
    }

    private static IInProcessTeammateTaskExecutor CreateTeammateExecutor(Action<InProcessTeammateDefinition> capture) {
        var mock = new Mock<IInProcessTeammateTaskExecutor>();
        mock.Setup(x => x.ExecuteTeammateAsync(It.IsAny<InProcessTeammateDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<InProcessTeammateDefinition, CancellationToken>((d, _) => capture(d))
            .ReturnsAsync(new AgentTaskResult {
                TaskId = "test",
                IsSuccess = true,
                Output = "",
                ExecutionTimeMs = 0,
                AgentId = "test-agent",
            });
        return mock.Object;
    }
}