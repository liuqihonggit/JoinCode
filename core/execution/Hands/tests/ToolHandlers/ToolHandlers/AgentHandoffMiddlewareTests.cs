namespace Tools.Handlers.Tests;

public class AgentHandoffMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldAppendWorktreeInfo_WhenWorktreeIsolationEnabled()
    {
        var worktreeManagerMock = new Mock<JoinCode.Abstractions.Interfaces.IAgentWorktreeManager>();
        var middleware = CreateMiddleware(worktreeManager: worktreeManagerMock.Object);

        var context = CreateContext(agentId: "agent-123");

        var session = new AgentWorktreeSession
        {
            AgentId = "agent-123",
            OriginalCwd = "/home/user/project",
            WorktreePath = "/home/user/project/.jccode/worktrees/agent-123",
            BranchName = "agent-agent-123",
            GitRootPath = "/home/user/project",
            CreatedAt = DateTime.UtcNow
        };

        worktreeManagerMock.Setup(x => x.IsWorktreeIsolationEnabled).Returns(true);
        worktreeManagerMock.Setup(x => x.GetWorktreeSessionAsync("agent-123", default))
            .ReturnsAsync(session);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        context.WorktreePath.Should().Be(session.WorktreePath);
        context.WorktreeBranch.Should().Be(session.BranchName);
        var text = context.Result!.GetTextContent();
        text.Should().Contain("worktreePath:");
        text.Should().Contain(session.WorktreePath);
        text.Should().Contain("worktreeBranch:");
        text.Should().Contain(session.BranchName);
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotAppendWorktreeInfo_WhenWorktreeDisabled()
    {
        var worktreeManagerMock = new Mock<JoinCode.Abstractions.Interfaces.IAgentWorktreeManager>();
        var middleware = CreateMiddleware(worktreeManager: worktreeManagerMock.Object);

        var context = CreateContext(agentId: "agent-123");

        worktreeManagerMock.Setup(x => x.IsWorktreeIsolationEnabled).Returns(false);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        context.WorktreePath.Should().BeNull();
        context.WorktreeBranch.Should().BeNull();
        context.Result!.GetTextContent().Should().NotContain("worktreePath:");
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotAppendWorktreeInfo_WhenNoSession()
    {
        var worktreeManagerMock = new Mock<JoinCode.Abstractions.Interfaces.IAgentWorktreeManager>();
        var middleware = CreateMiddleware(worktreeManager: worktreeManagerMock.Object);

        var context = CreateContext(agentId: "agent-123");

        worktreeManagerMock.Setup(x => x.IsWorktreeIsolationEnabled).Returns(true);
        worktreeManagerMock.Setup(x => x.GetWorktreeSessionAsync("agent-123", default))
            .ReturnsAsync((AgentWorktreeSession?)null);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        context.WorktreePath.Should().BeNull();
        context.WorktreeBranch.Should().BeNull();
        context.Result!.GetTextContent().Should().NotContain("worktreePath:");
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotAppendWorktreeInfo_WhenNoAgentId()
    {
        var worktreeManagerMock = new Mock<JoinCode.Abstractions.Interfaces.IAgentWorktreeManager>();
        var middleware = CreateMiddleware(worktreeManager: worktreeManagerMock.Object);

        var context = CreateContext(agentId: null);

        worktreeManagerMock.Setup(x => x.IsWorktreeIsolationEnabled).Returns(true);

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        context.WorktreePath.Should().BeNull();
    }

    private static AgentHandoffMiddleware CreateMiddleware(
        JoinCode.Abstractions.Interfaces.IAgentWorktreeManager? worktreeManager = null)
    {
        return new AgentHandoffMiddleware(
            handoffClassifier: null,
            worktreeManager: worktreeManager);
    }

    private static AgentToolContext CreateContext(string? agentId = null)
    {
        return new AgentToolContext
        {
            Description = "test agent",
            Prompt = "do something",
            AgentId = agentId,
            Succeeded = true
        };
    }
}
