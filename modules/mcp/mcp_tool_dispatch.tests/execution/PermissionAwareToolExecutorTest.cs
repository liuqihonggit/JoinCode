// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
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

    /// <summary>桩中间件 — 始终要求确认并带出触发原因规则内容（P5 数据面：WebFetch 的 domain:hostname）。</summary>
    private sealed class PendingWithRuleMiddleware : IMiddleware<ToolExecutionContext> {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct) {
            context.RequireConfirmation("confirm?", "domain:example.com");
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
        var json = result.GetFirstText();
        json.Should().NotBeNull();
        json.Should().Contain("\"type\":\"execution_failed\"");
        json.Should().Contain("boom");
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

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
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
    public async Task HandlePendingConfirmation_Unattended_DoesNotAskOrBlock_AndRegistersDeferredTicket() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        await using var pending = new PendingConfirmationQueue();

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new AlwaysPendingMiddleware()),
            CreatePermissionManager(PermissionMode.Unattended), confirmationHandler: confirmHandler.Object,
            pendingConfirmations: pending, logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("无人值守");
        confirmHandler.Verify(
            c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "后台没有宿主，不允许弹确认框");
        pending.HasPending.Should().BeFalse("票据登记后立即 Defer，不能留在队列里等待");
    }

    [Fact]
    public async Task HandlePendingConfirmation_Foreground_RegistersTicketAndResolvesAfterAnswer() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Allow));
        await using var pending = new PendingConfirmationQueue();

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingThenSuccessMiddleware()),
            CreatePermissionManager(PermissionMode.Auto), confirmationHandler: confirmHandler.Object,
            pendingConfirmations: pending, logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeFalse();
        pending.HasPending.Should().BeFalse("用户回答后票据必须结算，不能残留");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserDenies_ReturnsDeniedError() {        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Deny));

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new AlwaysPendingMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("\"type\":\"permission_denied\"");
    }

    // ---- TASK069-P3 拒绝反馈 ----

    [Fact]
    public async Task HandlePendingConfirmation_UserDeniesWithFeedback_CarriesUserWords() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Deny, "别删，改成备份"));

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new AlwaysPendingMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        // 用户原话进拒绝消息 — 只说「被拒绝」模型只能干等，带上说明它才知道换成哪种做法
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("别删，改成备份");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserDeniesWithoutFeedback_KeepsOriginalMessage() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Deny, "   "));

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new AlwaysPendingMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        // 纯空白反馈归一为 null → 文案与改造前逐字一致（现状零变化）
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("用户拒绝工具执行: test_tool");
        result.GetFirstText().Should().NotContain("说明");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserAllowsWithFeedback_AppendsToToolResult() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Allow, "只跑单测"));

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingThenSuccessMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        // 对齐基准 toolExecution.ts:1417-1429 — 允许附带的说明作为独立 text 追加到工具结果
        result.IsError.Should().BeFalse();
        result.Content.Should().Contain(c => c.Text == "只跑单测");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserAllows_RetriesAndReturnsSuccess() {        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Allow));

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
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.AlwaysAllow));
        var permissionManager = new Mock<IToolPermissionManager>();
        permissionManager.Setup(p => p.GetCurrentModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PermissionMode.Auto);

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingThenSuccessMiddleware()),
            permissionManager.Object, confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("success after confirm");
        // TASK070 P1/DSG046 — AlwaysAllow 持久化（无 command 参数 → 工具级规则 null）
        permissionManager.Verify(p => p.ApproveToolPermanentlyAsync("test_tool", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>TASK070 P1/DSG046 — AlwaysAllow 带 command 参数（Shell 类）→ 前缀规则 "command:*" 持久化。</summary>
    [Fact]
    public async Task HandlePendingConfirmation_UserAlwaysAllow_WithCommand_PersistsPrefixRule() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.AlwaysAllow));
        var permissionManager = new Mock<IToolPermissionManager>();
        permissionManager.Setup(p => p.GetCurrentModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PermissionMode.Auto);

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler, "Bash"), CreatePipeline(new PendingThenSuccessMiddleware()),
            permissionManager.Object, confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);
        var arguments = new Dictionary<string, JsonElement> { ["command"] = JsonDocument.Parse("\"npm run build\"").RootElement.Clone() };

        await executor.ExecuteAsync("Bash", arguments);

        permissionManager.Verify(
            p => p.ApproveToolPermanentlyAsync("Bash", "npm run build:*", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>TASK070 P1/DSG046 — Allow（本次允许）不触发持久化。</summary>
    [Fact]
    public async Task HandlePendingConfirmation_UserAllowOnce_DoesNotPersist() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Allow));
        var permissionManager = new Mock<IToolPermissionManager>();
        permissionManager.Setup(p => p.GetCurrentModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PermissionMode.Auto);

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingThenSuccessMiddleware()),
            permissionManager.Object, confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        await executor.ExecuteAsync("test_tool", []);

        permissionManager.Verify(p => p.ApproveToolPermanentlyAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task HandlePendingConfirmation_UserAllows_RaisesCompletionEvent() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Allow));

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

    /// <summary>桩中间件 — 要求确认但不设置 Result（Result 保持 null）</summary>
    private sealed class PendingWithoutResultMiddleware : IMiddleware<ToolExecutionContext> {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct) {
            context.PermissionDecision = PermissionDecision.PendingConfirmation;
            context.PermissionConfirmationPrompt = "confirm?";
            return Task.CompletedTask;
        }
    }

    /// <summary>桩中间件 — 第一次要求确认，第二次不设置 Result（retry 无结果）</summary>
    private sealed class PendingThenNoResultMiddleware : IMiddleware<ToolExecutionContext> {
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
            }
            // 第二次调用不设置 Result，保持 null
            return Task.CompletedTask;
        }
    }

    // === ExecuteAsync — OperationCanceledException 转为 JSON 结果（不再 rethrow） ===

    [Fact]
    public async Task ExecuteAsync_PipelineThrowsOperationCanceledException_ReturnsCancelledResult() {
        var handler = CreateStubHandler();
        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler),
            CreatePipeline(new ThrowingMiddleware(new OperationCanceledException())),
            CreatePermissionManager(), logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("\"type\":\"cancelled\"");
    }

    // === HandlePendingConfirmationAsync — 边缘分支 ===

    [Fact]
    public async Task HandlePendingConfirmation_NoHandlerAndNullResult_ReturnsDefaultDeniedError() {
        var handler = CreateStubHandler();
        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingWithoutResultMiddleware()),
            CreatePermissionManager(), confirmationHandler: null,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("\"type\":\"permission_required\"");
    }

    [Fact]
    public async Task HandlePendingConfirmation_UserAllows_RetryProducesNoResult_ReturnsNoResultError() {
        var handler = CreateStubHandler();
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Allow));

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingThenNoResultMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("no result");
    }

    // ---- TASK069-P5 触发原因透传 ----

    /// <summary>
    /// 待确认时把规则内容一并交给确认处理器 — 否则 CLI/TUI 对话框都只能显示「要确认」，
    /// 用户看不出是哪条规则命中的（基准 PermissionRuleExplanation 正是这个位置）。
    /// </summary>
    [Fact]
    public async Task HandlePendingConfirmation_PassesRuleContentToHandler() {
        var handler = CreateStubHandler();
        var seenRule = "未调用";
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, _, rule, _) => seenRule = rule)
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Deny));

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new PendingWithRuleMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("test_tool", []);

        result.IsError.Should().BeTrue();
        seenRule.Should().Be("domain:example.com");
    }

    /// <summary>无结构化规则来源时传 null（渲染层据此折叠原因区，不得编造规则名）。</summary>
    [Fact]
    public async Task HandlePendingConfirmation_WithoutRuleContent_PassesNull() {
        var handler = CreateStubHandler();
        var seenRule = "未调用";
        var confirmHandler = new Mock<IPermissionConfirmationHandler>();
        confirmHandler.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, _, rule, _) => seenRule = rule)
            .ReturnsAsync(PermissionConfirmResult.Of(PermissionConfirmAction.Deny));

        await using var executor = new PermissionAwareToolExecutor(
            CreateRegistry(handler), CreatePipeline(new AlwaysPendingMiddleware()),
            CreatePermissionManager(), confirmationHandler: confirmHandler.Object,
            logger: NullLogger<PermissionAwareToolExecutor>.Instance);

        await executor.ExecuteAsync("test_tool", []);

        seenRule.Should().BeNull();
    }
}
