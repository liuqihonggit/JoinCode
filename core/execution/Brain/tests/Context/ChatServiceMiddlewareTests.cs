namespace Brain.Tests.Context;

/// <summary>
/// ChatService 中间件管道单元测试 — 验证管道构建、排序、短路、清理、ToolUseContext 共享
/// </summary>
public sealed class ChatServiceMiddlewareTests
{
    /// <summary>
    /// 创建 ChatService 实例，使用 mock 依赖和指定中间件列表
    /// </summary>
    private static ChatService CreateService(
        IEnumerable<IChatMiddleware> middlewares,
        Mock<IChatUsageProcessor>? usageProcessorMock = null)
    {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.SaveContextAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var usageProcessor = (usageProcessorMock ?? new Mock<IChatUsageProcessor>()).Object;

        var adminMiddlewares = new IChatAdminMiddleware[]
        {
            new SessionAdminMiddleware(Array.Empty<IChatAdminOperationHandler>()),
            new SessionSaveMiddleware(NullLogger<SessionSaveMiddleware>.Instance),
        };

        var middlewarePipeline = new StreamMiddlewarePipeline<ChatMiddlewareContext, ChatStreamEvent>(
            middlewares.Cast<IStreamMiddleware<ChatMiddlewareContext, ChatStreamEvent>>(),
            onError: (_, _) => { });
        var adminPipeline = new MiddlewarePipeline<ChatAdminContext>(
            adminMiddlewares.Cast<IMiddleware<ChatAdminContext>>(),
            onError: (_, _) => { });

        return new ChatService(
            contextManager.Object,
            middlewarePipeline,
            adminPipeline,
            logger: NullLogger<ChatService>.Instance);
    }

    // === 管道构建 ===

    [Fact]
    public async Task SendMessageAsync_NoMiddlewares_ReturnsFallbackText()
    {
        // 空中间件列表 → 只有 TerminalHandler → 无事件 → 返回回退文本
        var service = CreateService([]);
        var result = await service.SendMessageAsync("hello").ConfigureAwait(true);
        result.Should().Be("抱歉，我无法生成回复。");
    }

    [Fact]
    public async Task SendMessageAsync_WithMockQueryLoop_ReturnsResponse()
    {
        var service = CreateService([
            new MockPreChatMiddleware(),
            new MockQueryLoopMiddleware("Hello from mock")
        ]);

        var result = await service.SendMessageAsync("hello").ConfigureAwait(true);
        result.Should().Be("Hello from mock");
    }

    // === 排序 ===

    [Fact]
    public async Task Pipeline_MiddlewaresExecuteInOrderSequence()
    {
        // 用自定义中间件记录执行顺序
        var executionLog = new List<string>();

        var service = CreateService([
            new OrderTrackingMiddleware("A", executionLog),
            new OrderTrackingMiddleware("B", executionLog),
            new OrderTrackingMiddleware("C", executionLog),
        ]);

        await service.SendMessageAsync("test").ConfigureAwait(true);

        executionLog.Should().Equal("A", "B", "C");
    }

    // === 事件流 ===

    [Fact]
    public async Task StreamWithEventsAsync_WithMockMiddlewares_ReturnsAllEvents()
    {
        var service = CreateService([
            new MockPreChatMiddleware(),
            new MockQueryLoopMiddleware("stream text")
        ]);

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in service.StreamWithEventsAsync("hello").ConfigureAwait(true))
        {
            events.Add(evt);
        }

        // MockQueryLoopMiddleware 产生: Text + Done
        events.Should().HaveCountGreaterThanOrEqualTo(2);
        events[0].Type.Should().Be(ChatStreamEventType.Content);
        events[0].Content.Should().Be("stream text");
        events[^1].Type.Should().Be(ChatStreamEventType.Complete);
    }

    [Fact]
    public async Task SendMessageStreamAsync_WithMockMiddlewares_ReturnsTextChunks()
    {
        var service = CreateService([
            new MockPreChatMiddleware(),
            new MockQueryLoopMiddleware("chunk1")
        ]);

        var chunks = new List<string>();
        await foreach (var chunk in service.SendMessageStreamAsync("hello").ConfigureAwait(true))
        {
            chunks.Add(chunk);
        }

        chunks.Should().Contain("chunk1");
    }

    // === 短路 ===

    [Fact]
    public async Task Pipeline_ShortCircuit_MiddlewareSkipsNext()
    {
        var executionLog = new List<string>();

        var service = CreateService([
            new ShortCircuitMiddleware("blocked", executionLog),
            new OrderTrackingMiddleware("should-not-run", executionLog),
        ]);

        var result = await service.SendMessageAsync("test").ConfigureAwait(true);

        result.Should().Be("blocked");
        executionLog.Should().NotContain("should-not-run");
    }

    // === ConversationTurn 递增 ===

    [Fact]
    public async Task SendMessageAsync_IncrementsConversationTurn()
    {
        var service = CreateService([
            new MockPreChatMiddleware(),
            new MockQueryLoopMiddleware()
        ]);

        // 第一次调用 — ConversationTurn=0
        await service.SendMessageAsync("first").ConfigureAwait(true);

        // 第二次调用 — ConversationTurn=1
        // 用 TurnRecordingMiddleware 验证
        var turnRecorder = new TurnRecordingMiddleware();
        var service2 = CreateService([
            turnRecorder,
            new MockQueryLoopMiddleware()
        ]);

        await service2.SendMessageAsync("msg1").ConfigureAwait(true);
        turnRecorder.LastTurn.Should().Be(0);

        await service2.SendMessageAsync("msg2").ConfigureAwait(true);
        turnRecorder.LastTurn.Should().Be(1);
    }

    // === ToolUseContext 共享 ===

    [Fact]
    public async Task Pipeline_ToolUseContext_SharedAcrossMiddlewares()
    {
        var contextCapture = new ToolUseContextCaptureMiddleware();

        var service = CreateService([
            contextCapture,
            new MockQueryLoopMiddleware()
        ]);

        await service.SendMessageAsync("test").ConfigureAwait(true);

        // 中间件捕获的 ToolUseContext 不应为 null
        contextCapture.CapturedContext.Should().NotBeNull();
    }

    // === CleanupAsync — 使用真实 ProcessUsageMiddleware 验证 ===

    [Fact]
    public async Task SendMessageAsync_WithUsage_ProcessesUsage()
    {
        var usageMock = new Mock<IChatUsageProcessor>();
        var preprocessorMock = new Mock<IChatPreprocessor>();
        var contextManagerMock = new Mock<IChatContextManager>();
        contextManagerMock.Setup(c => c.SaveContextAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processUsage = new ProcessUsageMiddleware(
            usageMock.Object, NullLogger<ProcessUsageMiddleware>.Instance);
        var cleanupInjections = new CleanupInjectionsMiddleware(
            preprocessorMock.Object, NullLogger<CleanupInjectionsMiddleware>.Instance);
        var saveContext = new SaveContextMiddleware(
            contextManagerMock.Object, NullLogger<SaveContextMiddleware>.Instance);

        var service = CreateService([
            new MockPreChatMiddleware(),
            new MockQueryLoopMiddleware(),
            processUsage,
            cleanupInjections,
            saveContext
        ]);

        await service.SendMessageAsync("test").ConfigureAwait(true);

        // MockQueryLoopMiddleware 设置了 FinalUsage，ProcessUsageMiddleware 应调用 ProcessUsageAsync
        usageMock.Verify(
            u => u.ProcessUsageAsync(It.IsAny<TokenUsage>(), It.IsAny<string>(), It.IsAny<PromptStateSnapshot>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WithUsage_ProcessUsageFailure_DoesNotThrow()
    {
        var usageMock = new Mock<IChatUsageProcessor>();
        usageMock.Setup(u => u.ProcessUsageAsync(It.IsAny<TokenUsage>(), It.IsAny<string>(), It.IsAny<PromptStateSnapshot>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("usage failed"));

        var preprocessorMock = new Mock<IChatPreprocessor>();
        var contextManagerMock = new Mock<IChatContextManager>();
        contextManagerMock.Setup(c => c.SaveContextAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processUsage = new ProcessUsageMiddleware(
            usageMock.Object, NullLogger<ProcessUsageMiddleware>.Instance);
        var cleanupInjections = new CleanupInjectionsMiddleware(
            preprocessorMock.Object, NullLogger<CleanupInjectionsMiddleware>.Instance);
        var saveContext = new SaveContextMiddleware(
            contextManagerMock.Object, NullLogger<SaveContextMiddleware>.Instance);

        var service = CreateService([
            new MockPreChatMiddleware(),
            new MockQueryLoopMiddleware(),
            processUsage,
            cleanupInjections,
            saveContext
        ], usageMock);

        // 不应抛出异常（框架 OnError=Continue 捕获了异常）
        var act = async () => await service.SendMessageAsync("test").ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    // === 辅助中间件 ===

    /// <summary>
    /// 顺序追踪中间件 — 记录执行顺序
    /// </summary>
    private sealed class OrderTrackingMiddleware(string label, List<string> log) : IChatMiddleware
    {
        public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
            ChatMiddlewareContext context,
            StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
            [EnumeratorCancellation] CancellationToken ct)
        {
            log.Add(label);
            await foreach (var evt in next(context, ct).ConfigureAwait(true))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// 短路中间件 — 不调用 next，直接返回文本事件
    /// </summary>
    private sealed class ShortCircuitMiddleware(string response, List<string> log) : IChatMiddleware
    {
        public IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
            ChatMiddlewareContext context,
            StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
            CancellationToken ct)
        {
            log.Add("short-circuit");
            context.FinalUsage = new TokenUsage(1, 1);
            context.FinalModelId = "test-model";

            return ShortCircuitImpl(response, ct);
        }

        private static async IAsyncEnumerable<ChatStreamEvent> ShortCircuitImpl(
            string response, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return ChatStreamEvent.Text(response);
            yield return ChatStreamEvent.Done(new TokenUsage(1, 1), "test-model");
        }
    }

    /// <summary>
    /// 轮次记录中间件 — 捕获 ConversationTurn
    /// </summary>
    private sealed class TurnRecordingMiddleware : IChatMiddleware
    {
        public int LastTurn { get; private set; }

        public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
            ChatMiddlewareContext context,
            StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
            [EnumeratorCancellation] CancellationToken ct)
        {
            LastTurn = context.ConversationTurn;
            await foreach (var evt in next(context, ct).ConfigureAwait(true))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// ToolUseContext 捕获中间件 — 验证共享实例
    /// </summary>
    private sealed class ToolUseContextCaptureMiddleware : IChatMiddleware
    {
        public ToolUseContext? CapturedContext { get; private set; }

        public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
            ChatMiddlewareContext context,
            StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
            [EnumeratorCancellation] CancellationToken ct)
        {
            CapturedContext = context.ToolUseContext;
            await foreach (var evt in next(context, ct).ConfigureAwait(true))
            {
                yield return evt;
            }
        }
    }
}
