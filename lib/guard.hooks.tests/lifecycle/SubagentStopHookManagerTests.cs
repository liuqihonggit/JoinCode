namespace Core.Tests.Hooks.Lifecycle;

public class SubagentStopHookManagerTests
{
    private readonly Mock<IHookOrchestrator> _orchestratorMock;
    private readonly SubagentStopHookManager _sut;

    public SubagentStopHookManagerTests()
    {
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _sut = new SubagentStopHookManager(_orchestratorMock.Object);
    }

    [Fact]
    public async Task OnSubagentStopAsync_NoHooksRegistered_ShouldProceed()
    {
        var context = CreateContext();
        SetupOrchestrator([]);

        var result = await _sut.OnSubagentStopAsync(context);

        result.ShouldProceed.Should().BeTrue();
    }

    [Fact]
    public async Task OnSubagentStopAsync_BlockingHook_ShouldReturnBlock()
    {
        var context = CreateContext();
        SetupOrchestrator([new HookResult { Outcome = HookOutcome.Blocking, Message = "blocked" }]);

        var result = await _sut.OnSubagentStopAsync(context);

        result.ShouldProceed.Should().BeFalse();
        result.Message.Should().Be("blocked");
    }

    [Fact]
    public async Task OnSubagentStopAsync_PreventContinuation_ShouldReturnBlock()
    {
        var context = CreateContext();
        SetupOrchestrator([new HookResult { PreventContinuation = true, Outcome = HookOutcome.Blocking, Message = "prevented" }]);

        var result = await _sut.OnSubagentStopAsync(context);

        result.ShouldProceed.Should().BeFalse();
        result.Message.Should().Be("prevented");
    }

    [Fact]
    public async Task OnSubagentStopAsync_NonBlockingHook_ShouldProceed()
    {
        var context = CreateContext();
        SetupOrchestrator([new HookResult { Outcome = HookOutcome.Success, Message = "ok" }]);

        var result = await _sut.OnSubagentStopAsync(context);

        result.ShouldProceed.Should().BeTrue();
    }

    [Fact]
    public async Task OnSubagentStopAsync_ShouldPassAgentIdInPayload()
    {
        var context = CreateContext(agentId: "agent-123");
        Dictionary<string, JsonElement>? capturedPayload = null;

        _orchestratorMock.Setup(o => o.ExecuteHooksAsync(
                HookEvent.SubagentStop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((HookEvent _, Dictionary<string, JsonElement> payload, string? _, string? _, CancellationToken _) =>
            {
                capturedPayload = payload;
                return AsyncEnumerable.Empty<HookResult>();
            });

        await _sut.OnSubagentStopAsync(context);

        capturedPayload.Should().NotBeNull();
        capturedPayload!["agentId"].GetString().Should().Be("agent-123");
        capturedPayload["agentType"].GetString().Should().Be("executor");
        capturedPayload["isSuccess"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task OnSubagentStopAsync_WithWorktreePath_ShouldIncludeInPayload()
    {
        var context = CreateContext(worktreePath: "/tmp/worktree-1");
        Dictionary<string, JsonElement>? capturedPayload = null;

        _orchestratorMock.Setup(o => o.ExecuteHooksAsync(
                HookEvent.SubagentStop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((HookEvent _, Dictionary<string, JsonElement> payload, string? _, string? _, CancellationToken _) =>
            {
                capturedPayload = payload;
                return AsyncEnumerable.Empty<HookResult>();
            });

        await _sut.OnSubagentStopAsync(context);

        capturedPayload.Should().NotBeNull();
        capturedPayload!.ContainsKey("worktreePath").Should().BeTrue();
        capturedPayload["worktreePath"].GetString().Should().Be("/tmp/worktree-1");
    }

    private static SubagentStopHookContext CreateContext(
        string agentId = "agent-001",
        string agentType = "executor",
        string? worktreePath = null,
        bool isSuccess = true)
    {
        return new SubagentStopHookContext
        {
            SessionId = "session-001",
            AgentId = agentId,
            AgentType = agentType,
            WorktreePath = worktreePath,
            IsSuccess = isSuccess,
        };
    }

    private void SetupOrchestrator(IReadOnlyList<HookResult> results)
    {
        _orchestratorMock.Setup(o => o.ExecuteHooksAsync(
                HookEvent.SubagentStop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(results));
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IReadOnlyList<T> list)
    {
        foreach (var item in list)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
