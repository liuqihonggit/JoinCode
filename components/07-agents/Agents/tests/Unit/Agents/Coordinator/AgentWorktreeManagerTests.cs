namespace Core.Tests.Agents.Coordinator;

public class AgentWorktreeManagerTests
{
    private readonly Mock<IAgentWorktreeService> _worktreeServiceMock;
    private readonly Mock<IHookOrchestrator> _hookOrchestratorMock;
    private readonly AgentWorktreeManager _manager;

    public AgentWorktreeManagerTests()
    {
        _worktreeServiceMock = new Mock<IAgentWorktreeService>();
        _hookOrchestratorMock = new Mock<IHookOrchestrator>();

        _manager = new AgentWorktreeManager(
            worktreeService: _worktreeServiceMock.Object,
            hookOrchestrator: _hookOrchestratorMock.Object,
            logger: NullLogger.Instance,
            enableWorktreeIsolation: true);
    }

    [Fact]
    public async Task CreateWorktreeAsync_ShouldReturnTrue_WhenServiceSucceeds()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));

        var result = await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);

        result.Should().BeTrue();
        _worktreeServiceMock.Verify(x => x.CreateAgentWorktreeAsync(agentId, null, null, default), Times.Once);
    }

    [Fact]
    public async Task CreateWorktreeAsync_ShouldReturnFalse_WhenServiceFails()
    {
        var agentId = "test-agent-1";

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.FailureResult("git not found"));

        var result = await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateWorktreeAsync_ShouldFireWorktreeCreatedEvent()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId);
        WorktreeEventArgs? firedArgs = null;
        _manager.WorktreeCreated += (_, args) => firedArgs = args;

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);

        firedArgs.Should().NotBeNull();
        firedArgs!.AgentId.Should().Be(agentId);
        firedArgs.WorktreePath.Should().Be(session.WorktreePath);
        firedArgs.BranchName.Should().Be(session.BranchName);
    }

    [Fact]
    public async Task CreateWorktreeAsync_ShouldReturnFalse_WhenIsolationDisabled()
    {
        var manager = new AgentWorktreeManager(
            worktreeService: _worktreeServiceMock.Object,
            enableWorktreeIsolation: false);

        var result = await manager.CreateWorktreeAsync("any-agent").ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldRemoveUnchangedWorktree()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));
        _worktreeServiceMock.Setup(x => x.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.HasUncommittedChangesAsync(session.WorktreePath, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.RemoveAgentWorktreeAsync(agentId, true, default))
            .ReturnsAsync(WorktreeCleanupResult.SuccessResult(true));

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        var result = await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);

        result.WasRemoved.Should().BeTrue();
        result.Kept.Should().BeFalse();
        result.WorktreePath.Should().BeNull();
        _worktreeServiceMock.Verify(x => x.RemoveAgentWorktreeAsync(agentId, true, default), Times.Once);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldKeepWorktreeWithChanges()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));
        _worktreeServiceMock.Setup(x => x.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, default))
            .ReturnsAsync(true);

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        var result = await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);

        result.Kept.Should().BeTrue();
        result.WorktreePath.Should().Be(session.WorktreePath);
        result.BranchName.Should().Be(session.BranchName);
        result.Reason.Should().Be("has_changes");
        _worktreeServiceMock.Verify(x => x.RemoveAgentWorktreeAsync(It.IsAny<string>(), It.IsAny<bool>(), default), Times.Never);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldKeepWorktreeWithUncommittedChanges()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));
        _worktreeServiceMock.Setup(x => x.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.HasUncommittedChangesAsync(session.WorktreePath, default))
            .ReturnsAsync(true);

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        var result = await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);

        result.Kept.Should().BeTrue();
        result.Reason.Should().Be("has_changes");
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldKeepHookBasedWorktree()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId, hookBased: true);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        var result = await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);

        result.Kept.Should().BeTrue();
        result.WorktreePath.Should().Be(session.WorktreePath);
        result.Reason.Should().Be("hook-based");
        _worktreeServiceMock.Verify(x => x.RemoveAgentWorktreeAsync(It.IsAny<string>(), It.IsAny<bool>(), default), Times.Never);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldKeepWorktree_WhenRemoveFails()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));
        _worktreeServiceMock.Setup(x => x.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.HasUncommittedChangesAsync(session.WorktreePath, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.RemoveAgentWorktreeAsync(agentId, true, default))
            .ReturnsAsync(WorktreeCleanupResult.FailureResult("permission denied"));

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        var result = await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);

        result.Kept.Should().BeTrue();
        result.Reason.Should().Be("remove_failed");
        result.WorktreePath.Should().Be(session.WorktreePath);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldReturnNotIsolated_WhenDisabled()
    {
        var manager = new AgentWorktreeManager(
            worktreeService: _worktreeServiceMock.Object,
            enableWorktreeIsolation: false);

        var result = await manager.CleanupWorktreeAsync("any-agent").ConfigureAwait(true);

        result.Should().Be(WorktreeCleanupDetail.NotIsolated);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldReturnNoSession_WhenNoSessionExists()
    {
        var result = await _manager.CleanupWorktreeAsync("nonexistent-agent").ConfigureAwait(true);

        result.Should().Be(WorktreeCleanupDetail.NoSession);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldFireWorktreeCleanedEvent()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId);
        WorktreeEventArgs? cleanedArgs = null;
        _manager.WorktreeCleaned += (_, args) => cleanedArgs = args;

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));
        _worktreeServiceMock.Setup(x => x.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.HasUncommittedChangesAsync(session.WorktreePath, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.RemoveAgentWorktreeAsync(agentId, true, default))
            .ReturnsAsync(WorktreeCleanupResult.SuccessResult(true));

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);

        cleanedArgs.Should().NotBeNull();
        cleanedArgs!.AgentId.Should().Be(agentId);
    }

    [Fact]
    public async Task GetWorktreeSessionAsync_ShouldReturnSession_AfterCreate()
    {
        var agentId = "test-agent-1";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        var result = await _manager.GetWorktreeSessionAsync(agentId).ConfigureAwait(true);

        result.Should().NotBeNull();
        result!.AgentId.Should().Be(agentId);
    }

    [Fact]
    public async Task GetWorktreeSessionAsync_ShouldReturnNull_WhenNotCreated()
    {
        var result = await _manager.GetWorktreeSessionAsync("nonexistent").ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public void IsWorktreeIsolationEnabled_ShouldReflectConstructorParameter()
    {
        var enabled = new AgentWorktreeManager(_worktreeServiceMock.Object, enableWorktreeIsolation: true);
        var disabled = new AgentWorktreeManager(_worktreeServiceMock.Object, enableWorktreeIsolation: false);

        enabled.IsWorktreeIsolationEnabled.Should().BeTrue();
        disabled.IsWorktreeIsolationEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task CreateWorktreeAsync_ShouldTriggerWorktreeCreateHook()
    {
        var agentId = "test-agent-hook";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));

        var hookResults = new List<HookResult>();
        _hookOrchestratorMock.Setup(x => x.ExecuteHooksAsync(
                HookEvent.WorktreeCreate,
                It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), default))
            .Returns(ToAsyncEnumerable(hookResults));

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);

        // Hook 是 fire-and-forget (Task.Run)，等待一小段时间
        await Task.Delay(100).ConfigureAwait(true);

        _hookOrchestratorMock.Verify(x => x.ExecuteHooksAsync(
            HookEvent.WorktreeCreate,
            It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CleanupWorktreeAsync_ShouldTriggerWorktreeRemoveHook()
    {
        var agentId = "test-agent-hook-remove";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));
        _worktreeServiceMock.Setup(x => x.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.HasUncommittedChangesAsync(session.WorktreePath, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.RemoveAgentWorktreeAsync(agentId, true, default))
            .ReturnsAsync(WorktreeCleanupResult.SuccessResult(true));

        var hookResults = new List<HookResult>();
        _hookOrchestratorMock.Setup(x => x.ExecuteHooksAsync(
                HookEvent.WorktreeRemove,
                It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), default))
            .Returns(ToAsyncEnumerable(hookResults));

        await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);

        await Task.Delay(100).ConfigureAwait(true);

        _hookOrchestratorMock.Verify(x => x.ExecuteHooksAsync(
            HookEvent.WorktreeRemove,
            It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(),
            It.IsAny<string?>(), It.IsAny<string?>(), default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task FullWorkflow_CreateAndCleanupUnchanged_ShouldRemoveWorktree()
    {
        var agentId = "workflow-agent";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));
        _worktreeServiceMock.Setup(x => x.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.HasUncommittedChangesAsync(session.WorktreePath, default))
            .ReturnsAsync(false);
        _worktreeServiceMock.Setup(x => x.RemoveAgentWorktreeAsync(agentId, true, default))
            .ReturnsAsync(WorktreeCleanupResult.SuccessResult(true));

        var created = await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        created.Should().BeTrue();

        var sessionAfterCreate = await _manager.GetWorktreeSessionAsync(agentId).ConfigureAwait(true);
        sessionAfterCreate.Should().NotBeNull();

        var cleanup = await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);
        cleanup.WasRemoved.Should().BeTrue();
        cleanup.Kept.Should().BeFalse();

        var sessionAfterCleanup = await _manager.GetWorktreeSessionAsync(agentId).ConfigureAwait(true);
        sessionAfterCleanup.Should().BeNull();
    }

    [Fact]
    public async Task FullWorkflow_CreateAndCleanupWithChanges_ShouldKeepWorktree()
    {
        var agentId = "workflow-agent-changes";
        var session = CreateSession(agentId);

        _worktreeServiceMock.Setup(x => x.CreateAgentWorktreeAsync(agentId, null, null, default))
            .ReturnsAsync(WorktreeCreateResult.SuccessResult(session));
        _worktreeServiceMock.Setup(x => x.HasUnpushedCommitsAsync(session.WorktreePath, session.BaseCommitSha, default))
            .ReturnsAsync(true);

        var created = await _manager.CreateWorktreeAsync(agentId).ConfigureAwait(true);
        created.Should().BeTrue();

        var cleanup = await _manager.CleanupWorktreeAsync(agentId).ConfigureAwait(true);
        cleanup.Kept.Should().BeTrue();
        cleanup.WorktreePath.Should().Be(session.WorktreePath);
        cleanup.BranchName.Should().Be(session.BranchName);
        cleanup.Reason.Should().Be("has_changes");
    }

    private static AgentWorktreeSession CreateSession(string agentId, bool hookBased = false)
    {
        return new AgentWorktreeSession
        {
            AgentId = agentId,
            OriginalCwd = "/home/user/project",
            WorktreePath = $"/home/user/project/.jccode/worktrees/{agentId}",
            BranchName = $"agent-{agentId}",
            GitRootPath = "/home/user/project",
            OriginalBranch = "main",
            BaseCommitSha = "abc123def456",
            CreatedAt = DateTime.UtcNow,
            Existed = false,
            HookBased = hookBased
        };
    }

    private sealed class NullLogger : ILogger
    {
        public static NullLogger Instance { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
