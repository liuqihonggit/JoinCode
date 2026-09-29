namespace McpToolRegistry.Tests;

/// <summary>
/// PermissionAwareToolExecutor 确定性测试 — 主执行路径、确认流程(用真实管道+桩中间件)
/// </summary>
public class PermissionAwareToolExecutorTest {
    private static IToolHandler CreateStubHandler(string name = "test_tool") {
        var handler = new Mock<IToolHandler>();
        handler.SetupGet(h => h.Name).Returns(name);
        handler.SetupGet(h => h.Description).Returns("stub");
        handler.SetupGet(h => h.InputSchema).Returns(new ToolSchema());
        handler.SetupGet(h => h.Kind).Returns(ToolKind.Mcp);
        return handler.Object;
    }

    private static IToolRegistry CreateRegistry(IToolHandler? handler, string toolName = "test_tool") {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetToolAsync(toolName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(handler);
        return registry.Object;
    }

    private static IToolPermissionManager CreatePermissionManager(PermissionMode mode = PermissionMode.Auto) {
        var pm = new Mock<IToolPermissionManager>();
        pm.Setup(p => p.GetCurrentModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(mode);
        return pm.Object;
    }

    private static MiddlewarePipeline<ToolExecutionContext> CreatePipeline(params IMiddleware<ToolExecutionContext>[] middlewares)
        => new(middlewares);

    /// <summary>桩中间件 — 设置固定 Result</summary>
    private sealed class ResultSettingMiddleware(ToolResult result) : IMiddleware<ToolExecutionContext> {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct) {
            context.Result = result;
            return Task.CompletedTask;
        }
    }

    /// <summary>桩中间件 — 抛异常</summary>
    private sealed class ThrowingMiddleware(Exception ex) : IMiddleware<ToolExecutionContext> {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
            => throw ex;
    }

    /// <summary>桩中间件 — 第一次要求确认，第二次设置成功结果</summary>
    private sealed class PendingThenSuccessMiddleware : IMiddleware<ToolExecutionContext> {
        private int _callCount;
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct) {
            var count = Interlocked.Increment(ref _callCount);
            if (count == 1) {
                context.PermissionDecision = PermissionDecision.PendingConfirmation;
                context.PermissionConfirmationPrompt = "confirm?";
                context.Result = new ToolResult {
                    Content = [new() { Type = ToolContentType.Text, Text = "need confirm" }],
                    IsError = true
                };
            } else {
                context.Result = new ToolResult {
                    Content = [new() { Type = ToolContentType.Text, Text = "success after confirm" }],
                    IsError = false
                };
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>桩中间件 — 始终要求确认</summary>
    private sealed class AlwaysPendingMiddleware : IMiddleware<ToolExecutionContext> {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct) {
            context.PermissionDecision = PermissionDecision.PendingConfirmation;
            context.PermissionConfirmationPrompt = "confirm?";
            context.Result = new ToolResult {
                Content = [new() { Type = ToolContentType.Text, Text = "need confirm" }],
                IsError = true
            };
            return Task.CompletedTask;
        }
    }

    // === ExecuteAsync ===

    [Fact]
    public async Task ExecuteAsync_HandlerNotFound_ReturnsErrorResult() {
        var registry = CreateRegistry(handler: null, "missing_tool");
        await using var executor = new PermissionAwareToolExecutor(
            registry, CreatePipeline(), CreatePermissionManager(),
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("missing_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_PipelineProducesSuccess_ReturnsSuccessResult() {
        var handler = CreateStubHandler();
        var success = new ToolResult {
            Content = [new() { Type = ToolContentType.Text, Text = "done" }],
            IsError = false
        };
        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new ResultSettingMiddleware(success)),
            CreatePermissionManager(), logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Be("done");
    }

    [Fact]
    public async Task ExecuteAsync_PipelineThrows_ReturnsExceptionErrorResult() {
        var handler = CreateStubHandler();
        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler),
            CreatePipeline(new ThrowingMiddleware(new InvalidOperationException("boom"))),
            CreatePermissionManager(), logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("Error executing tool");
        result.GetFirstText().Should().Contain("boom");
    }

    [Fact]
    public async Task ExecuteAsync_PipelineCompletesWithoutResult_ReturnsNoResultError() {
        var handler = CreateStubHandler();
        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(),
            CreatePermissionManager(), logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("no result");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessResult_RaisesToolExecutionCompletedEvent() {
        var handler = CreateStubHandler();
        var success = new ToolResult {
            Content = [new() { Type = ToolContentType.Text, Text = "ok" }],
            IsError = false
        };
        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new ResultSettingMiddleware(success)),
            CreatePermissionManager(), logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        ToolExecutionCompletedEventArgs? eventArgs = null;
        executor.ToolExecutionCompleted += (_, e) => eventArgs = e;

        await executor.ExecuteAsync("test_tool", []);

        eventArgs.Should().NotBeNull();
        eventArgs!.ToolName.Should().Be("test_tool");
        eventArgs.IsError.Should().BeFalse();
    }

    // === HandlePendingConfirmationAsync (通过 ExecuteAsync 间接测确认流程) ===

    [Fact]
    public async Task HandlePendingConfirmation_NoConfirmationHandler_ReturnsPendingResult() {
        var handler = CreateStubHandler();
        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new AlwaysPendingMiddleware()),
            CreatePermissionManager(), confirmationHandler: null,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("need confirm");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserDenies_ReturnsDeniedError() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PermissionConfirmAction.Deny);

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new AlwaysPendingMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("拒绝");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserAllows_RetriesAndReturnsSuccess() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PermissionConfirmAction.Allow);

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingThenSuccessMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("success after confirm");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserAlwaysAllow_RetriesAndReturnsSuccess() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PermissionConfirmAction.AlwaysAllow);

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingThenSuccessMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("success after confirm");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserAllows_RaisesCompletionEvent() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PermissionConfirmAction.Allow);

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingThenSuccessMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        ToolExecutionCompletedEventArgs? eventArgs = null;
        executor.ToolExecutionCompleted += (_, e) => eventArgs = e;

        await executor.ExecuteAsync("test_tool", []);

        eventArgs.Should().NotBeNull();
        eventArgs!.ToolName.Should().Be("test_tool");
        eventArgs.IsError.Should().BeFalse();
    }
}
