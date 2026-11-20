// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Context;

/// <summary>
/// TASK103 单循环 — QueryLoopMiddleware 把循环委托给 IQueryEngine。
/// 覆盖事件适配、历史去重与增量回写、执行设置/闸门透传、轮内 steer。
/// </summary>
public sealed class QueryLoopQueryBackendTests {
    /// <summary>可编排输出的假查询引擎 — 记录传入历史与选项，按脚本向历史追加消息。</summary>
    private sealed class FakeQueryEngine : IQueryEngine {
        private readonly List<QueryStreamChunk> _chunks = [];

        public List<QueryOptions?> ReceivedOptions { get; } = [];
        public List<int> HistoryCountsBeforeUser { get; } = [];
        public List<int> HistoryCountsAfterHook { get; } = [];
        public Action<MessageList>? OnQuery { get; set; }

        public void Enqueue(params QueryStreamChunk[] chunks) => _chunks.AddRange(chunks);

        public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("测试只走流式查询");

        public IAsyncEnumerable<QueryStreamChunk> QueryAsync(
            string userInput, MessageList chatHistory, CancellationToken cancellationToken = default) =>
            QueryAsync(userInput, chatHistory, options: null, cancellationToken);

        public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
            string userInput,
            MessageList chatHistory,
            QueryOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            ReceivedOptions.Add(options);
            HistoryCountsBeforeUser.Add(chatHistory.Count);
            chatHistory.AddUserMessage(userInput);
            if (options?.OnIterationStart is { } onIterationStart)
                await onIterationStart(chatHistory, cancellationToken);
            HistoryCountsAfterHook.Add(chatHistory.Count);
            OnQuery?.Invoke(chatHistory);

            foreach (var chunk in _chunks) {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }

        public IQueryService GetChatCompletionService() => throw new NotSupportedException();

        public IChatClient GetKernel() => throw new NotSupportedException();
    }

    [Fact]
    public async Task QueryBackend_MapsEngineChunks_AndSkipsDuplicateUserMessage() {
        var assistantWrites = new List<string>();
        var contextManager = CreateContextManager(
            new MessageList([new ApiMessage(MessageRole.User, "hello")]),
            assistantWrites);

        var engine = new FakeQueryEngine();
        engine.OnQuery = history => history.AddAssistantMessage("answer");
        engine.Enqueue(
            new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = "hello" },
            new QueryStreamChunk { Type = AgentStreamChunkType.ToolCallStart, ToolName = "Read", ToolCallId = "call-1", ToolArguments = """{"path":"a.cs"}""" },
            new QueryStreamChunk { Type = AgentStreamChunkType.ToolCallEnd, ToolName = "Read", ToolCallId = "call-1", ToolResultText = "ok" },
            new QueryStreamChunk { Type = AgentStreamChunkType.Error, Content = "警告示例" },
            new QueryStreamChunk { Type = AgentStreamChunkType.Complete, Usage = new TokenUsage(10, 20), ModelId = "gpt-test", StopReason = "end_turn", TotalToolCalls = 1, TotalTurns = 2 });

        await using var middleware = CreateMiddleware(contextManager, engine);

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in middleware.InvokeAsync(
            CreateContext(), (_, _) => AsyncEnumerableEmpty<ChatStreamEvent>(), CancellationToken.None)) {
            events.Add(evt);
        }

        events.Select(e => e.Type).Should().Equal(
            ChatStreamEventType.Content,
            ChatStreamEventType.ToolCallStart,
            ChatStreamEventType.ToolCallEnd,
            ChatStreamEventType.SystemNotice,
            ChatStreamEventType.Complete);
        events[0].Content.Should().Be("hello");
        events[1].ToolName.Should().Be("Read");
        events[2].ToolResultText.Should().Be("ok");
        if (events[4].Usage is not { } doneUsage)
            throw new InvalidOperationException("Complete 事件缺少 usage");
        doneUsage.PromptTokens.Should().Be(10);
        events[4].ModelId.Should().Be("gpt-test");
        events[4].StopReason.Should().Be("end_turn");

        engine.HistoryCountsBeforeUser.Should().ContainSingle().Which.Should().Be(0, "当前用户消息已由 Preprocessor 落库，查询引擎会重新加入");
        assistantWrites.Should().Equal("answer");
        contextManager.Verify(c => c.AddUserMessageAsync(
            It.IsAny<string>(), It.IsAny<MessageOriginKind?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryBackend_PassesGateOverridesAndSessionLevelModelEffort() {
        var contextManager = CreateContextManager(new MessageList(), assistantWrites: []);
        var engine = new FakeQueryEngine();
        engine.Enqueue(new QueryStreamChunk {
            Type = AgentStreamChunkType.Complete,
            StopReason = "end_turn",
            TotalTurns = 1
        });
        await using var middleware = CreateMiddleware(contextManager, engine);

        var settings = new ChatOptions {
            Temperature = 0.2f,
            ToolChoice = ToolChoice.AutoInvoke,
            PromptCacheKey = "session-1"
        };
        var context = new ChatMiddlewareContext {
            Message = "hello",
            ToolUseContext = new ToolUseContext { ModelOverride = "gpt-x", Effort = "high" },
            ExecutionSettings = settings,
            RequireRoundGate = true,
            SessionId = "session-1"
        };

        await foreach (var _ in middleware.InvokeAsync(
            context, (_, _) => AsyncEnumerableEmpty<ChatStreamEvent>(), CancellationToken.None)) {
        }

        var options = engine.ReceivedOptions.Should().ContainSingle().Subject;
        if (options is null)
            throw new InvalidOperationException("查询引擎未收到 QueryOptions");
        options.RequireRoundGate.Should().BeTrue();
        options.SessionId.Should().Be("session-1");
        options.ModelId.Should().Be("gpt-x");
        options.EffortLevel.Should().Be(EffortLevel.High);
        options.ExecutionOverrides.Should().BeSameAs(settings);
    }

    [Fact]
    public async Task QueryBackend_DrainsSteerAtIterationStart_EmitsEventAndPersistsUserMessage() {
        var userWrites = new List<string>();
        var contextManager = CreateContextManager(new MessageList(), assistantWrites: []);
        contextManager.Setup(c => c.AddUserMessageAsync(It.IsAny<string>(), It.IsAny<MessageOriginKind?>(), It.IsAny<CancellationToken>()))
            .Callback<string, MessageOriginKind?, CancellationToken>((content, _, _) => userWrites.Add(content))
            .Returns(Task.CompletedTask);

        var mailbox = new SteerInputMailbox();
        mailbox.Enqueue("session-steer", "STEER 输入");

        var engine = new FakeQueryEngine();
        engine.Enqueue(
            new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = "收到" },
            new QueryStreamChunk { Type = AgentStreamChunkType.Complete, StopReason = "end_turn", TotalTurns = 1 });

        await using var middleware = CreateMiddleware(contextManager, engine, steerMailbox: mailbox);

        var context = new ChatMiddlewareContext {
            Message = "hello",
            ToolUseContext = new ToolUseContext(),
            SessionId = "session-steer"
        };

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in middleware.InvokeAsync(
            context, (_, _) => AsyncEnumerableEmpty<ChatStreamEvent>(), CancellationToken.None)) {
            events.Add(evt);
        }

        events.Should().Contain(e => e.Type == ChatStreamEventType.SteerInput && e.Content == "STEER 输入");
        engine.HistoryCountsAfterHook.Should().ContainSingle().Which.Should().Be(2, "当前用户消息 + 步首 steer 输入");
        userWrites.Should().Contain("STEER 输入", "steer 消息必须随历史增量落库");
        engine.ReceivedOptions[0].Should().NotBeNull();
    }

    private static ChatMiddlewareContext CreateContext() => new() {
        Message = "hello",
        ToolUseContext = new ToolUseContext()
    };

    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>([EnumeratorCancellation] CancellationToken ct = default) {
        await Task.CompletedTask;
        yield break;
    }

    private static Mock<IChatContextManager> CreateContextManager(MessageList history, List<string> assistantWrites) {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(history);
        contextManager.Setup(c => c.SyncDiscoveredToolsFromHistoryAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddUserMessageAsync(It.IsAny<string>(), It.IsAny<MessageOriginKind?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddAssistantToolCallMessageAsync(
                It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddToolResultMessageAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>(),
                It.IsAny<IReadOnlyList<ToolContent>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((content, _) => assistantWrites.Add(content))
            .Returns(Task.CompletedTask);
        return contextManager;
    }

    private static QueryLoopMiddleware CreateMiddleware(
        Mock<IChatContextManager> contextManager,
        FakeQueryEngine engine,
        ISteerInputMailbox? steerMailbox = null) =>
        new(
            new BackgroundNotificationHandler(contextManager.Object),
            new TelemetryRecorder(),
            contextManager.Object,
            engine,
            steerMailbox: steerMailbox);
}
