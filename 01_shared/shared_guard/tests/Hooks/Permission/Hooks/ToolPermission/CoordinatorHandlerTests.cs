
namespace Core.Tests.Hooks.ToolPermission;

/// <summary>
/// CoordinatorHandler 测试
/// </summary>
public class CoordinatorHandlerTests
{
    private readonly CoordinatorHandler _handler;
    private readonly Mock<IHookOrchestrator> _orchestratorMock;
    private readonly PermissionHookExecutor _hookExecutor;
    private readonly TestPermissionLogger _logger;

    public CoordinatorHandlerTests()
    {
        _handler = new CoordinatorHandler(NullLogger<CoordinatorHandler>.Instance);
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _hookExecutor = new PermissionHookExecutor(_orchestratorMock.Object, NullLogger<PermissionHookExecutor>.Instance);
        _logger = new TestPermissionLogger();
    }

    [Fact]
    public async Task HandleAsync_NoHooksNoClassifier_ShouldReturnNull()
    {
        var ctx = CreateTestContext();
        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<HookResult>());

        var @params = new CoordinatorPermissionParams
        {
            Context = ctx,
            HookExecutor = _hookExecutor
        };

        var result = await _handler.HandleAsync(@params).ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_HookReturnsAllow_ShouldReturnAllowDecision()
    {
        var ctx = CreateTestContext();
        var updatedInput = new Dictionary<string, JsonElement> { ["test_key"] = JsonElementHelper.FromString("test_value") };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .Returns(new[]
            {
                new HookResult
                {
                    Outcome = HookOutcome.Success,
                    PermissionRequestResult = new PermissionAllowResult { UpdatedInput = updatedInput }
                }
            }.ToAsyncEnumerable());

        var @params = new CoordinatorPermissionParams
        {
            Context = ctx,
            HookExecutor = _hookExecutor
        };

        var result = await _handler.HandleAsync(@params).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeOfType<PermissionAllowDecision>();
        ((PermissionAllowDecision)result!).UpdatedInput.Should().ContainKey("test_key");
    }

    [Fact]
    public async Task HandleAsync_HookReturnsDeny_ShouldReturnDenyDecision()
    {
        var ctx = CreateTestContext();

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .Returns(new[]
            {
                new HookResult
                {
                    Outcome = HookOutcome.Success,
                    PermissionRequestResult = new PermissionDenyResult { Message = "Test denial reason" }
                }
            }.ToAsyncEnumerable());

        var @params = new CoordinatorPermissionParams
        {
            Context = ctx,
            HookExecutor = _hookExecutor
        };

        var result = await _handler.HandleAsync(@params).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeOfType<PermissionDenyDecision>();
        ((PermissionDenyDecision)result!).Message.Should().Contain("Test denial reason");
    }

    [Fact]
    public async Task HandleAsync_WithUpdatedInput_ShouldUseUpdatedInput()
    {
        var ctx = CreateTestContext();
        var updatedInput = new Dictionary<string, JsonElement>
        {
            ["updated_key"] = JsonElementHelper.FromString("updated_value")
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .Returns(new[]
            {
                new HookResult
                {
                    Outcome = HookOutcome.Success,
                    PermissionRequestResult = new PermissionAllowResult { UpdatedInput = updatedInput }
                }
            }.ToAsyncEnumerable());

        var @params = new CoordinatorPermissionParams
        {
            Context = ctx,
            UpdatedInput = updatedInput,
            HookExecutor = _hookExecutor
        };

        var result = await _handler.HandleAsync(@params).ConfigureAwait(true);

        result.Should().BeOfType<PermissionAllowDecision>();
        ((PermissionAllowDecision)result!).UpdatedInput.Should().ContainKey("updated_key");
    }

    private PermissionContext CreateTestContext()
    {
        return new PermissionContext(
            "test_tool",
            new Dictionary<string, JsonElement> { ["test_key"] = JsonElementHelper.FromString("test_value") },
            "msg_123",
            "tool_use_123",
            _logger);
    }

    #region Test Helpers

    private class TestPermissionLogger : IPermissionLogger
    {
        public List<string> Logs { get; } = new();

        public void LogPermissionDecision(PermissionLogContext context, PermissionDecisionArgs args)
        {
            Logs.Add($"Decision: {args.Decision} for {context.ToolName}");
        }

        public void LogPermissionCancelled(PermissionLogContext context)
        {
            Logs.Add($"Cancelled: {context.ToolName}");
        }

        public void LogCodeEditToolDecision(string toolName, string decision, string source, string? language = null)
        {
            Logs.Add($"CodeEdit: {toolName} = {decision} ({source})");
        }
    }

    #endregion
}
