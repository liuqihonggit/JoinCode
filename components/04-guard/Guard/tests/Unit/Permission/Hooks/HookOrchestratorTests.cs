
namespace Core.Tests.Hooks;

/// <summary>
/// HookOrchestrator 集成测试
/// </summary>
public class HookOrchestratorTests
{
    private readonly HookOrchestrator _orchestrator;
    private readonly Mock<IHookConfigurationManager> _configManagerMock;
    private readonly Mock<ISessionHookManagerInternal> _sessionManagerMock;
    private readonly Mock<IHookEventBroadcaster> _broadcasterMock;
    private readonly Mock<IAsyncHookRegistry> _asyncRegistryMock;
    private readonly Mock<IHookConditionEvaluator> _conditionEvaluatorMock;

    public HookOrchestratorTests()
    {
        _configManagerMock = new Mock<IHookConfigurationManager>();
        _sessionManagerMock = new Mock<ISessionHookManagerInternal>();
        _broadcasterMock = new Mock<IHookEventBroadcaster>();
        _asyncRegistryMock = new Mock<IAsyncHookRegistry>();
        _conditionEvaluatorMock = new Mock<IHookConditionEvaluator>();

        var executorFactory = new HookExecutorFactory();
        executorFactory.RegisterExecutor(new FunctionHookExecutor());

        _conditionEvaluatorMock
            .Setup(e => e.EvaluateAsync(It.IsAny<string?>(), It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _configManagerMock
            .Setup(m => m.GetHooksForEventAsync(It.IsAny<HookEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SourcedHookConfig>());

        _sessionManagerMock
            .Setup(m => m.GetSessionHooksAsync(It.IsAny<string>(), It.IsAny<HookEvent?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SourcedHookConfig>());

        _sessionManagerMock
            .Setup(m => m.GetSessionFunctionHooksAsync(It.IsAny<string>(), It.IsAny<HookEvent?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FunctionHook>());

        _orchestrator = new HookOrchestrator(
            _configManagerMock.Object,
            executorFactory,
            _sessionManagerMock.Object,
            _broadcasterMock.Object,
            _asyncRegistryMock.Object,
            _conditionEvaluatorMock.Object);
    }

    [Fact]
    public async Task ExecuteHooksAsync_NoHooks_ShouldReturnEmpty()
    {
        _configManagerMock
            .Setup(m => m.GetHooksForEventAsync(It.IsAny<HookEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SourcedHookConfig>());

        var input = new HookInput
        {
            Event = HookEvent.PreToolUse,
            ToolName = ShellToolNameConstants.ShellExecute,
            Payload = new Dictionary<string, JsonElement>()
        };

        var results = await _orchestrator.ExecuteHooksAsync(input).ToListAsync().ConfigureAwait(true);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteHooksAsync_WithFunctionHook_ShouldExecuteAndReturnResult()
    {
        var hookExecuted = false;
        var functionHook = new FunctionHook
        {
            Id = "test-hook",
            Callback = (input, ct) =>
            {
                hookExecuted = true;
                return Task.FromResult(HookResult.Success());
            }
        };

        _sessionManagerMock
            .Setup(m => m.GetSessionFunctionHooksAsync(It.IsAny<string>(), It.IsAny<HookEvent?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FunctionHook> { functionHook });

        var input = new HookInput
        {
            Event = HookEvent.PreToolUse,
            ToolName = ShellToolNameConstants.ShellExecute,
            SessionId = "test-session",
            Payload = new Dictionary<string, JsonElement>()
        };

        var results = await _orchestrator.ExecuteHooksAsync(input).ToListAsync().ConfigureAwait(true);

        hookExecuted.Should().BeTrue();
        results.Should().HaveCount(1);
        results[0].Outcome.Should().Be(HookOutcome.Success);
    }

    [Fact]
    public async Task ExecuteHooksAsync_WithBlockingResult_ShouldStopExecution()
    {
        var firstHookExecuted = false;
        var secondHookExecuted = false;

        var hook1 = new FunctionHook
        {
            Id = "hook-1",
            Callback = (input, ct) =>
            {
                firstHookExecuted = true;
                return Task.FromResult(HookResult.Blocking("error", "cmd"));
            }
        };

        var hook2 = new FunctionHook
        {
            Id = "hook-2",
            Callback = (input, ct) =>
            {
                secondHookExecuted = true;
                return Task.FromResult(HookResult.Success());
            }
        };

        _sessionManagerMock
            .Setup(m => m.GetSessionFunctionHooksAsync(It.IsAny<string>(), It.IsAny<HookEvent?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FunctionHook> { hook1, hook2 });

        var input = new HookInput
        {
            Event = HookEvent.PreToolUse,
            ToolName = ShellToolNameConstants.ShellExecute,
            SessionId = "test-session",
            Payload = new Dictionary<string, JsonElement>()
        };

        var results = await _orchestrator.ExecuteHooksAsync(input).ToListAsync().ConfigureAwait(true);

        firstHookExecuted.Should().BeTrue();
        secondHookExecuted.Should().BeFalse();
        results.Should().HaveCount(1);
        results[0].Outcome.Should().Be(HookOutcome.Blocking);
    }

    [Fact]
    public async Task ExecuteHooksAsync_WithConditionNotMet_ShouldSkipHook()
    {
        _conditionEvaluatorMock
            .Setup(e => e.EvaluateAsync(It.IsAny<string?>(), It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var hookExecuted = false;
        var functionHook = new FunctionHook
        {
            Id = "test-hook",
            If = "Bash(git *)",
            Callback = (input, ct) =>
            {
                hookExecuted = true;
                return Task.FromResult(HookResult.Success());
            }
        };

        _sessionManagerMock
            .Setup(m => m.GetSessionFunctionHooksAsync(It.IsAny<string>(), It.IsAny<HookEvent?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FunctionHook> { functionHook });

        var input = new HookInput
        {
            Event = HookEvent.PreToolUse,
            ToolName = ShellToolNameConstants.ShellExecute,
            SessionId = "test-session",
            Payload = new Dictionary<string, JsonElement>()
        };

        var results = await _orchestrator.ExecuteHooksAsync(input).ToListAsync().ConfigureAwait(true);

        hookExecuted.Should().BeFalse();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteHooksAsync_WithOnceHook_ShouldRemoveAfterExecution()
    {
        var hookExecuted = false;
        var functionHook = new FunctionHook
        {
            Id = "once-hook",
            Once = true,
            Callback = (input, ct) =>
            {
                hookExecuted = true;
                return Task.FromResult(HookResult.Success());
            }
        };

        _sessionManagerMock
            .Setup(m => m.GetSessionFunctionHooksAsync(It.IsAny<string>(), It.IsAny<HookEvent?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FunctionHook> { functionHook });

        _sessionManagerMock
            .Setup(m => m.RemoveFunctionHookAsync(It.IsAny<string>(), It.IsAny<HookEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var input = new HookInput
        {
            Event = HookEvent.PreToolUse,
            ToolName = ShellToolNameConstants.ShellExecute,
            SessionId = "test-session",
            Payload = new Dictionary<string, JsonElement>()
        };

        var results = await _orchestrator.ExecuteHooksAsync(input).ToListAsync().ConfigureAwait(true);

        hookExecuted.Should().BeTrue();
        _sessionManagerMock.Verify(
            m => m.RemoveFunctionHookAsync("test-session", HookEvent.PreToolUse, "once-hook", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteHooksAsync_SimplifiedOverload_ShouldWork()
    {
        _configManagerMock
            .Setup(m => m.GetHooksForEventAsync(It.IsAny<HookEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SourcedHookConfig>());

        var results = await _orchestrator.ExecuteHooksAsync(
            HookEvent.PreToolUse,
            new Dictionary<string, JsonElement> { ["test"] = JsonElementHelper.FromString("value") },
            ShellToolNameConstants.ShellExecute,
            "session-1").ToListAsync().ConfigureAwait(true);

        results.Should().BeEmpty();
        _configManagerMock.Verify(
            m => m.GetHooksForEventAsync(HookEvent.PreToolUse, ShellToolNameConstants.ShellExecute, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
