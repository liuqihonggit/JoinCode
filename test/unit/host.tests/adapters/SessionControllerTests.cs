namespace Host.Tests.Adapters;

public sealed class SessionControllerTests {
    [Fact]
    public async Task StreamResponseAsync_Success_ReturnsSuccessResult() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Text("Hello "),
            ChatStreamEvent.Text("World"),
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test input", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello World", result.Response);
        Assert.Equal("Hello World", controller.LastResponse);
    }

    [Fact]
    public async Task StreamResponseAsync_ToolEvents_DispatchedToConsumer() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.ToolStart("read_file", "call1", "{\"path\":\"test.cs\"}"),
            ChatStreamEvent.ToolEnd("read_file", "file content", "call1", false),
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Single(consumer.ToolStarts);
        Assert.Equal("read_file", consumer.ToolStarts[0].toolName);
        Assert.Single(consumer.ToolEnds);
        Assert.Equal("read_file", consumer.ToolEnds[0].toolName);
        Assert.False(consumer.ToolEnds[0].isError);
    }

    [Fact]
    public async Task StreamResponseAsync_Thinking_DispatchedToConsumer() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Thinking("let me think..."),
            ChatStreamEvent.Text("answer"),
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal("answer", result.Response);
        Assert.Single(consumer.ThinkingEvents);
        Assert.Equal("let me think...", consumer.ThinkingEvents[0]);
    }

    [Fact]
    public async Task StreamResponseAsync_Timeout_ReturnsTimeoutResult() {
        var chatService = CreateMockChatService(
            _ => new OperationCanceledException());
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task StreamResponseAsync_Error_ReturnsErrorResult() {
        var chatService = CreateMockChatService(
            _ => throw new InvalidOperationException("API error"));
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.Equal("API error", result.ErrorMessage);
    }

    [Fact]
    public async Task StreamResponseAsync_LoopDetected_DispatchedToConsumer() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.LoopDetected(3, 0, "repeated pattern"),
            ChatStreamEvent.Text("ok"),
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Single(consumer.LoopDetectedEvents);
        Assert.Equal(3, consumer.LoopDetectedEvents[0].triggerCount);
    }

    [Fact]
    public async Task StreamResponseAsync_ToolProgress_DispatchedToConsumer() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.ToolProgress("web_search", "query_update", "searching..."),
            ChatStreamEvent.Text("ok"),
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Single(consumer.ToolProgressEvents);
        Assert.Equal("web_search", consumer.ToolProgressEvents[0].toolName);
        Assert.Equal("query_update", consumer.ToolProgressEvents[0].progressType);
    }

    [Fact]
    public async Task StreamResponseAsync_TimingSummary_DispatchedToConsumer() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.TimingSummary("TTFT: 1.2s, Total: 3.5s"),
            ChatStreamEvent.Text("ok"),
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Single(consumer.TimingSummaryEvents);
        Assert.Equal("TTFT: 1.2s, Total: 3.5s", consumer.TimingSummaryEvents[0]);
    }

    [Fact]
    public async Task StreamResponseAsync_Done_StoresThinkingContent() {
        var thinkingStore = new Mock<IThinkingStore>();
        thinkingStore.Setup(s => s.StoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddSingleton(thinkingStore.Object);
        var sp = services.BuildServiceProvider();

        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Thinking("deep thought"),
            ChatStreamEvent.Text("answer"),
            ChatStreamEvent.Done(modelId: "gpt-4o")
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session", sp);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        thinkingStore.Verify(s => s.StoreAsync("test-session", "deep thought", "gpt-4o", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StreamResponseAsync_EmptyEvents_ReturnsErrorNotSilentSuccess() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("空回复", result.ErrorMessage);
    }

    [Fact]
    public async Task StreamResponseAsync_ToolOnlyTurn_EmptyTextStaysSuccess() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.ToolStart("read_file", "call1", "{}"),
            ChatStreamEvent.ToolEnd("read_file", "content", "call1", false),
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task StreamResponseAsync_ThinkingOnlyTurn_EmptyTextStaysSuccess() {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Thinking("思考中"),
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Stop_SetsIsRunningFalse() {
        var chatService = CreateMockChatService(Array.Empty<ChatStreamEvent>());
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        Assert.True(controller.IsRunning);
        controller.Stop();
        Assert.False(controller.IsRunning);
    }

    /// <summary>Done 携带真实 usage → 发射 OnContextUsage（M8-7 — 权威算法口径：
    /// IContextWindowResolver 窗口 + ICompactService.CalculateWarningState.PercentLeft）。</summary>
    [Fact]
    public async Task StreamResponseAsync_DoneWithUsage_EmitsContextUsageViaCompactAlgorithm() {
        var compact = new Mock<ICompactService>();
        compact.Setup(c => c.CalculateWarningState(150_000, 200_000))
            .Returns(new CompactWarningState {
                PercentLeft = 37,
                IsAboveWarningThreshold = false,
                IsAboveErrorThreshold = false,
                IsAboveAutoCompactThreshold = false,
                IsAtBlockingLimit = false,
                IsAboveSoftCompactThreshold = false,
            });
        var resolver = new Mock<IContextWindowResolver>();
        resolver.Setup(r => r.ResolveCurrentContextWindow()).Returns(200_000);
        var services = new ServiceCollection();
        services.AddSingleton(compact.Object);
        services.AddSingleton(resolver.Object);
        var sp = services.BuildServiceProvider();

        var events = new List<ChatStreamEvent> {
            ChatStreamEvent.Text("ok"),
            ChatStreamEvent.Done(usage: new TokenUsage(150_000, 500)),
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session", sp);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        var usage = Assert.Single(consumer.ContextUsages);
        Assert.Equal(150_000, usage.promptTokens);
        Assert.Equal(200_000, usage.contextWindowTokens);
        Assert.Equal(37, usage.percentLeft);
    }

    /// <summary>Done 携带 usage 但解析器/压缩服务不可达（无 sp）→ 朴素兜底（200K 窗口 + 窗口百分比）。</summary>
    [Fact]
    public async Task StreamResponseAsync_DoneWithUsage_NoServices_FallsBackToNaivePercent() {
        var events = new List<ChatStreamEvent> {
            ChatStreamEvent.Text("ok"),
            ChatStreamEvent.Done(usage: new TokenUsage(100_000, 200)),
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        var usage = Assert.Single(consumer.ContextUsages);
        Assert.Equal(100_000, usage.promptTokens);
        Assert.Equal(200_000, usage.contextWindowTokens);
        Assert.Equal(50, usage.percentLeft);
    }

    /// <summary>Done 无 usage（mock/无计量路径）→ 不发射 OnContextUsage（防噪音）。</summary>
    [Fact]
    public async Task StreamResponseAsync_DoneWithoutUsage_DoesNotEmitContextUsage() {
        var events = new List<ChatStreamEvent> {
            ChatStreamEvent.Text("ok"),
            ChatStreamEvent.Done(),
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Empty(consumer.ContextUsages);
    }

    [Fact]
    public async Task StreamResponseAsync_MainAgentPath_CallsSaveContext() {
        var queryEngine = CreateMockQueryEngine();
        await using var agent = new AgentBase(
            task: string.Empty,
            options: null,
            queryEngine: queryEngine,
            logger: null,
            name: "main",
            role: AgentRole.Coordinator);

        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.SaveContextAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(contextManager.Object);
        var sp = services.BuildServiceProvider();

        var chatService = CreateMockChatService(Array.Empty<ChatStreamEvent>());
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session", sp, mainAgent: agent);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        contextManager.Verify(c => c.SaveContextAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "主代理路径应在完成后调用 SaveContextAsync 持久化上下文");
    }

    [Fact]
    public async Task StreamResponseAsync_MainAgentPath_CallsPreprocessor() {
        var queryEngine = CreateMockQueryEngine();
        await using var agent = new AgentBase(
            task: string.Empty,
            options: null,
            queryEngine: queryEngine,
            logger: null,
            name: "main",
            role: AgentRole.Coordinator);

        var preprocessor = new Mock<IChatPreprocessor>();
        var fileContextService = new Mock<IChatFileContextService>();

        var services = new ServiceCollection();
        services.AddSingleton(preprocessor.Object);
        services.AddSingleton(fileContextService.Object);
        var sp = services.BuildServiceProvider();

        var chatService = CreateMockChatService(Array.Empty<ChatStreamEvent>());
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session", sp, mainAgent: agent);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        preprocessor.Verify(p => p.AnalyzeAndInjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "主代理路径应在执行前调用 AnalyzeAndInjectAsync 做预处理");
        fileContextService.Verify(f => f.UpdateFileContext(It.IsAny<string>()), Times.AtLeastOnce,
            "主代理路径应在执行前调用 UpdateFileContext 更新文件上下文");
    }

    /// <summary>
    /// showTurnDuration 接线（对齐 TS 默认 true 显示回合耗时）：无 WorkflowConfig 时默认显示。
    /// </summary>
    [Fact]
    public async Task StreamResponseAsync_MainAgentPath_Default_ShowsTimingSummary() {
        var queryEngine = CreateMockQueryEngine();
        await using var agent = new AgentBase(
            task: string.Empty,
            options: null,
            queryEngine: queryEngine,
            logger: null,
            name: "main",
            role: AgentRole.Coordinator);

        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var chatService = CreateMockChatService(Array.Empty<ChatStreamEvent>());
        var consumer = new RecordingEventConsumer();
        var controller = new SessionController(chatService, consumer, new TurnDiffService(), "test-session", sp, mainAgent: agent);

        await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        consumer.TimingSummaryEvents.Should().Contain(s => s.Contains("Total:"),
            "showTurnDuration 默认 true（对齐 TS）应显示回合耗时");
    }

    /// <summary>showTurnDuration=false 时抑制回合耗时显示（debuglog 开启时仍显示）。</summary>
    [Fact]
    public async Task StreamResponseAsync_MainAgentPath_ShowTurnDurationFalse_SuppressesTimingSummary() {
        var queryEngine = CreateMockQueryEngine();
        await using var agent = new AgentBase(
            task: string.Empty,
            options: null,
            queryEngine: queryEngine,
            logger: null,
            name: "main",
            role: AgentRole.Coordinator);

        var services = new ServiceCollection();
        services.AddSingleton(new WorkflowConfig { ShowTurnDuration = false });
        var sp = services.BuildServiceProvider();

        var chatService = CreateMockChatService(Array.Empty<ChatStreamEvent>());
        var consumer = new RecordingEventConsumer();
        var controller = new SessionController(chatService, consumer, new TurnDiffService(), "test-session", sp, mainAgent: agent);

        await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        consumer.TimingSummaryEvents.Should().NotContain(s => s.Contains("Total:"),
            "showTurnDuration=false 应抑制回合耗时显示");
    }

    private static IQueryEngine CreateMockQueryEngine() {
        var mock = new Mock<IQueryEngine>();
        var chunks = new QueryStreamChunk[]
        {
            new() { Type = AgentStreamChunkType.Content, Content = "Hello" },
            new() { Type = AgentStreamChunkType.Complete, ModelId = "test-model" }
        };
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, CancellationToken ct) => EmitChunksAsync(chunks, ct));
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, QueryOptions? opts, CancellationToken ct) => EmitChunksAsync(chunks, ct));
        return mock.Object;
    }

    private static async IAsyncEnumerable<QueryStreamChunk> EmitChunksAsync(
        IReadOnlyList<QueryStreamChunk> chunks,
        [EnumeratorCancellation] CancellationToken ct) {
        foreach (var chunk in chunks) {
            ct.ThrowIfCancellationRequested();
            yield return chunk;
        }
    }

    private static IChatService CreateMockChatService(IReadOnlyList<ChatStreamEvent> events) {
        var mock = new Mock<IChatService>();
        mock.Setup(s => s.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string input, CancellationToken ct) => EmitEventsAsync(events, ct));
        return mock.Object;
    }

    private static IChatService CreateMockChatService(Func<string, Exception> throwFunc, int delayMs = 0) {
        var mock = new Mock<IChatService>();
        mock.Setup(s => s.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string input, CancellationToken ct) => ThrowEventsAsync(throwFunc, input, delayMs, ct));
        return mock.Object;
    }

    private static async IAsyncEnumerable<ChatStreamEvent> EmitEventsAsync(
        IReadOnlyList<ChatStreamEvent> events,
        [EnumeratorCancellation] CancellationToken ct) {
        foreach (var evt in events) {
            ct.ThrowIfCancellationRequested();
            yield return evt;
        }
    }

    private static async IAsyncEnumerable<ChatStreamEvent> ThrowEventsAsync(
        Func<string, Exception> throwFunc,
        string input,
        int delayMs,
        [EnumeratorCancellation] CancellationToken ct) {
        if (delayMs > 0) {
            await Task.Delay(delayMs, ct).ConfigureAwait(true);
        }
        throw throwFunc(input);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private sealed class RecordingEventConsumer : IEventConsumer {
        public List<string> TextEvents { get; } = [];
        public List<string> ThinkingEvents { get; } = [];
        public List<(string toolName, string? toolCallId, string? arguments)> ToolStarts { get; } = [];
        public List<(string toolName, string? resultText, string? toolCallId, bool isError)> ToolEnds { get; } = [];
        public List<(string toolName, string progressType, string? progressMessage)> ToolProgressEvents { get; } = [];
        public List<(int triggerCount, int loopStartIndex, string? repeatedPattern)> LoopDetectedEvents { get; } = [];
        public List<string> TimingSummaryEvents { get; } = [];
        public List<(TokenUsage? usage, string? modelId)> DoneEvents { get; } = [];
        public List<(int promptTokens, int contextWindowTokens, int percentLeft)> ContextUsages { get; } = [];

        public void OnText(string content) => TextEvents.Add(content);
        public void OnThinking(string thinking) => ThinkingEvents.Add(thinking);
        public void OnToolStart(string toolName, string? toolCallId, string? arguments) => ToolStarts.Add((toolName, toolCallId, arguments));
        public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) => ToolEnds.Add((toolName, resultText, toolCallId, isError));
        public void OnToolProgress(string toolName, string progressType, string? progressMessage) => ToolProgressEvents.Add((toolName, progressType, progressMessage));
        public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) => LoopDetectedEvents.Add((triggerCount, loopStartIndex, repeatedPattern));
        public void OnTimingSummary(string summary) => TimingSummaryEvents.Add(summary);
        public void OnDone(TokenUsage? usage, string? modelId) => DoneEvents.Add((usage, modelId));
        public void OnContextUsage(int promptTokens, int contextWindowTokens, int percentLeft) => ContextUsages.Add((promptTokens, contextWindowTokens, percentLeft));
    }
}