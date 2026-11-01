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

    /// <summary>AggregateException（流式+非流式降级均失败）应解包内部异常消息，
    /// 不显示 "Both streaming and non-streaming fallback failed" 原始英文包装消息。</summary>
    [Fact]
    public async Task StreamResponseAsync_AggregateException_UnwrapsInnerMessages() {
        var inner1 = new System.Net.Http.HttpRequestException("流式请求失败 (HTTP 500)");
        var inner2 = new System.Net.Http.HttpRequestException("非流式降级也失败 (HTTP 500)");
        var aggEx = new AggregateException("Both streaming and non-streaming fallback failed", inner1, inner2);
        var chatService = CreateMockChatService(_ => aggEx);
        var consumer = new RecordingEventConsumer();
        var controller = new SessionController(chatService, consumer, new TurnDiffService(), "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("流式和非流式请求均失败", result.ErrorMessage);
        Assert.Contains("流式请求失败 (HTTP 500)", result.ErrorMessage);
        Assert.Contains("非流式降级也失败 (HTTP 500)", result.ErrorMessage);
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

    /// <summary>mainAgent 路径应在完成后写 transcript 增量条目（对齐 TranscriptPersistMiddleware —
    /// mainAgent 绕过 ChatService 管道，PostProcessMainAgentAsync 手动补 transcript 持久化）。</summary>
    [Fact]
    public async Task StreamResponseAsync_MainAgentPath_WritesTranscriptEntries() {
        var queryEngine = CreateMockQueryEngine();
        await using var agent = new AgentBase(
            task: string.Empty, options: null, queryEngine: queryEngine, logger: null,
            name: "main", role: AgentRole.Coordinator);

        var contextManager = new Mock<IChatContextManager>();
        contextManager.SetupGet(c => c.CurrentMessageCount).Returns(0);
        contextManager.SetupGet(c => c.SessionId).Returns("test-session");
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        contextManager.Setup(c => c.SaveContextAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList([
                new ApiMessage { Role = MessageRole.User, Content = "test" },
                new ApiMessage { Role = MessageRole.Assistant, Content = "Hello" },
            ]));

        var transcriptService = new Mock<ITranscriptService>();
        IReadOnlyList<TranscriptEntry>? capturedEntries = null;
        transcriptService.Setup(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, IReadOnlyList<TranscriptEntry> entries, CancellationToken _) => capturedEntries = entries)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(contextManager.Object);
        services.AddSingleton(transcriptService.Object);
        var sp = services.BuildServiceProvider();

        var chatService = CreateMockChatService(Array.Empty<ChatStreamEvent>());
        var consumer = new RecordingEventConsumer();
        var controller = new SessionController(chatService, consumer, new TurnDiffService(), "test-session", sp, mainAgent: agent);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded, result.ErrorMessage);
        transcriptService.Verify(t => t.AppendEntriesAsync("test-session", It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()), Times.Once,
            "主代理路径应在完成后写 transcript 增量条目");
        Assert.NotNull(capturedEntries);
        if (capturedEntries is not { } entries) return;
        Assert.Equal(2, entries.Count);
        Assert.Equal("user", entries[0].Role);
        Assert.Equal("test", entries[0].Content);
        Assert.Equal("assistant", entries[1].Role);
        Assert.Equal("Hello", entries[1].Content);
        Assert.Equal("test-model", entries[1].ModelId);
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

    /// <summary>Done usage 超阈值 → 回合后异步自动压缩（TASK070 C4/DSG046）：
    /// SendMessageAsync 生成摘要 → CompactHistoryAsync 压实 → OnCompactCompleted(isAuto:true)
    /// （OriginalTokens 为 Done 权威 PromptTokens）；LastAutoCompactTask 测试观察点消除时序不确定性。</summary>
    [Fact]
    public async Task StreamResponseAsync_DoneUsageAboveThreshold_RunsAutoCompactInBackground() {
        var (chatService, compactCalls) = CreateAutoCompactCapableChatService(
            events: [ChatStreamEvent.Text("ok"), ChatStreamEvent.Done(usage: new TokenUsage(150_000, 500))],
            historyMessages: 3,
            summaryText: "结构化摘要文本，用于压缩后的上下文恢复");
        var compact = new Mock<ICompactService>();
        compact.Setup(c => c.ShouldAutoCompact(150_000, 200_000)).Returns(true);
        compact.Setup(c => c.CalculateWarningState(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new CompactWarningState {
                PercentLeft = 25,
                IsAboveWarningThreshold = false,
                IsAboveErrorThreshold = false,
                IsAboveAutoCompactThreshold = false,
                IsAtBlockingLimit = false,
                IsAboveSoftCompactThreshold = false,
            });
        var resolver = new Mock<IContextWindowResolver>();
        resolver.Setup(r => r.ResolveCurrentContextWindow()).Returns(200_000);
        var sp = new ServiceCollection()
            .AddSingleton(compact.Object)
            .AddSingleton(resolver.Object)
            .BuildServiceProvider();
        var consumer = new RecordingEventConsumer();
        var controller = new SessionController(chatService, consumer, new TurnDiffService(), "test-session", sp);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);
        Assert.True(result.Succeeded, result.ErrorMessage);

        Assert.NotNull(controller.LastAutoCompactTask);
        await controller.LastAutoCompactTask.ConfigureAwait(true);

        Assert.Single(compactCalls);
        var completed = Assert.Single(consumer.CompactCompleted);
        Assert.Equal(3, completed.messagesSummarized);
        Assert.Equal(150_000, completed.originalTokens);
        Assert.Equal("结构化摘要文本，用于压缩后的上下文恢复".Length / 4, completed.compressedTokens);
        Assert.True(completed.isAuto);
    }

    /// <summary>usage 未达阈值 → 不触发压缩（无 LastAutoCompactTask、无 CompactHistory 调用、无事件）。</summary>
    [Fact]
    public async Task StreamResponseAsync_UsageBelowThreshold_SkipsAutoCompact() {
        var (chatService, compactCalls) = CreateAutoCompactCapableChatService(
            events: [ChatStreamEvent.Text("ok"), ChatStreamEvent.Done(usage: new TokenUsage(10_000, 500))],
            historyMessages: 3,
            summaryText: "unused");
        var compact = new Mock<ICompactService>();
        compact.Setup(c => c.ShouldAutoCompact(10_000, 200_000)).Returns(false);
        compact.Setup(c => c.CalculateWarningState(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new CompactWarningState {
                PercentLeft = 25,
                IsAboveWarningThreshold = false,
                IsAboveErrorThreshold = false,
                IsAboveAutoCompactThreshold = false,
                IsAtBlockingLimit = false,
                IsAboveSoftCompactThreshold = false,
            });
        var resolver = new Mock<IContextWindowResolver>();
        resolver.Setup(r => r.ResolveCurrentContextWindow()).Returns(200_000);
        var sp = new ServiceCollection()
            .AddSingleton(compact.Object)
            .AddSingleton(resolver.Object)
            .BuildServiceProvider();
        var consumer = new RecordingEventConsumer();
        var controller = new SessionController(chatService, consumer, new TurnDiffService(), "test-session", sp);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded, $"T={result.TimedOut} C={result.WasCancelled} E={result.ErrorMessage}");
        Assert.Null(controller.LastAutoCompactTask);
        Assert.Empty(compactCalls);
        Assert.Empty(consumer.CompactCompleted);
    }

    /// <summary>ICompactService 不可达（无 sp）→ 自动压缩静默跳过（不抛、不触发）。</summary>
    [Fact]
    public async Task StreamResponseAsync_NoCompactService_SkipsAutoCompact() {
        var (chatService, compactCalls) = CreateAutoCompactCapableChatService(
            events: [ChatStreamEvent.Text("ok"), ChatStreamEvent.Done(usage: new TokenUsage(150_000, 500))],
            historyMessages: 3,
            summaryText: "unused");
        var consumer = new RecordingEventConsumer();
        var controller = new SessionController(chatService, consumer, new TurnDiffService(), "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded, $"T={result.TimedOut} C={result.WasCancelled} E={result.ErrorMessage}");
        Assert.Null(controller.LastAutoCompactTask);
        Assert.Empty(compactCalls);
    }

    /// <summary>自动压缩后台失败（摘要 LLM 调用抛错）→ 回合产出不受影响（已成功返回），无 compact_completed 事件。</summary>
    [Fact]
    public async Task StreamResponseAsync_AutoCompactFailure_TurnStillSucceeds() {
        var chatService = new Mock<IChatService>();
        chatService.Setup(s => s.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string input, CancellationToken ct) => EmitEventsAsync(
                [ChatStreamEvent.Text("ok"), ChatStreamEvent.Done(usage: new TokenUsage(150_000, 500))], ct));
        chatService.Setup(s => s.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessageRecord> { new() { Role = "user", Content = "q" } });
        chatService.Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM 不可达"));
        var compact = new Mock<ICompactService>();
        compact.Setup(c => c.ShouldAutoCompact(150_000, 200_000)).Returns(true);
        compact.Setup(c => c.CalculateWarningState(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new CompactWarningState {
                PercentLeft = 25,
                IsAboveWarningThreshold = false,
                IsAboveErrorThreshold = false,
                IsAboveAutoCompactThreshold = false,
                IsAtBlockingLimit = false,
                IsAboveSoftCompactThreshold = false,
            });
        var resolver = new Mock<IContextWindowResolver>();
        resolver.Setup(r => r.ResolveCurrentContextWindow()).Returns(200_000);
        var sp = new ServiceCollection()
            .AddSingleton(compact.Object)
            .AddSingleton(resolver.Object)
            .BuildServiceProvider();
        var consumer = new RecordingEventConsumer();
        var controller = new SessionController(chatService.Object, consumer, new TurnDiffService(), "test-session", sp);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);
        Assert.True(result.Succeeded, result.ErrorMessage);

        Assert.NotNull(controller.LastAutoCompactTask);
        await controller.LastAutoCompactTask.ConfigureAwait(true);

        Assert.Empty(consumer.CompactCompleted);
        Assert.Equal("ok", result.Response);
    }

    private static (IChatService Chat, List<string> CompactCalls) CreateAutoCompactCapableChatService(
        IReadOnlyList<ChatStreamEvent> events, int historyMessages, string summaryText) {
        var compactCalls = new List<string>();
        var mock = new Mock<IChatService>();
        mock.Setup(s => s.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string input, CancellationToken ct) => EmitEventsAsync(events, ct));
        mock.Setup(s => s.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, historyMessages)
                .Select(_ => new ApiMessageRecord { Role = "user", Content = "消息内容" })
                .ToList());
        mock.Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaryText);
        mock.Setup(s => s.CompactHistoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string summary, CancellationToken ct) => {
                compactCalls.Add(summary);
                return Task.CompletedTask;
            });
        return (mock.Object, compactCalls);
    }

    /// <summary>TASK070 E2/DSG046 — 主代理 RetryNotifier 绑定：构造期把 consumer.OnRetry 挂上
    /// AgentBase（→ BuildChatOptions → QueryOptions.OnRetry → QueryEngine 重试前回调），触发即上屏。</summary>
    [Fact]
    public async Task Constructor_WithMainAgent_BindsRetryNotifierToConsumer() {
        var queryEngine = CreateMockQueryEngine();
        await using var agent = new AgentBase(
            task: string.Empty, options: null, queryEngine: queryEngine, logger: null,
            name: "main", role: AgentRole.Coordinator);
        var consumer = new RecordingEventConsumer();
        _ = new SessionController(
            CreateMockChatService(Array.Empty<ChatStreamEvent>()), consumer, new TurnDiffService(), "test-session", mainAgent: agent);

        agent.RetryNotifier.Should().NotBeNull("主代理路径构造期应绑定 RetryNotifier");
        Assert.NotNull(agent.RetryNotifier);
        agent.RetryNotifier(new QueryRetryNotification("连接超时", 1, 3, 1000));

        var retry = Assert.Single(consumer.Retries);
        Assert.Equal(("连接超时", 1, 3, 1000), retry);
    }

    /// <summary>无主代理（ChatService 直连路径）不绑定 — RetryNotifier 仅主代理语义。</summary>
    [Fact]
    public void Constructor_WithoutMainAgent_NoRetryBinding() {
        var consumer = new RecordingEventConsumer();
        // 无 mainAgent 构造不抛即可（无 agent 可挂）
        _ = new SessionController(CreateMockChatService(Array.Empty<ChatStreamEvent>()), consumer, new TurnDiffService(), "test-session");
        Assert.Empty(consumer.Retries);
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
        public List<(int messagesSummarized, long originalTokens, long compressedTokens, bool isAuto)> CompactCompleted { get; } = [];
        public List<(string reason, int attempt, int maxRetries, int delayMs)> Retries { get; } = [];

        public void OnText(string content) => TextEvents.Add(content);
        public void OnThinking(string thinking) => ThinkingEvents.Add(thinking);
        public void OnToolStart(string toolName, string? toolCallId, string? arguments) => ToolStarts.Add((toolName, toolCallId, arguments));
        public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) => ToolEnds.Add((toolName, resultText, toolCallId, isError));
        public void OnToolProgress(string toolName, string progressType, string? progressMessage) => ToolProgressEvents.Add((toolName, progressType, progressMessage));
        public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) => LoopDetectedEvents.Add((triggerCount, loopStartIndex, repeatedPattern));
        public void OnTimingSummary(string summary) => TimingSummaryEvents.Add(summary);
        public void OnDone(TokenUsage? usage, string? modelId) => DoneEvents.Add((usage, modelId));
        public void OnContextUsage(int promptTokens, int contextWindowTokens, int percentLeft) => ContextUsages.Add((promptTokens, contextWindowTokens, percentLeft));
        public void OnCompactCompleted(int messagesSummarized, long originalTokens, long compressedTokens, bool isAuto) => CompactCompleted.Add((messagesSummarized, originalTokens, compressedTokens, isAuto));
        public void OnRetry(string reason, int attempt, int maxRetries, int delayMs) => Retries.Add((reason, attempt, maxRetries, delayMs));
    }
}