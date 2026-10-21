namespace JoinCode.Host.Tests.Adapters;

/// <summary>
/// SessionController 子代理事件路由测试（DSG041 批1 — L3 协议扩展）：
/// IEventConsumer 新增三子代理回调（OnSubagentStarted/OnSubagentToolUse/OnSubagentFinished），
/// ProcessEvent 对 IsSubAgentActivity 事件（AgentId 非 null）前置分流路由到子代理回调——
/// 普通工具事件零回归（AgentId null 走既有 onToolStart/onToolEnd 路径）。
/// 事件源侧（AgentStreamExecutionMiddleware 等已 Emit）与通道接通（批2）无关——本批只测
/// "事件到达 ProcessEvent 后的正确分流"（经 ChatService 路径喂入，对齐既有 mock 先例）。
/// </summary>
public sealed class SessionControllerSubagentEventTests
{
    /// <summary>构造无主代理 SessionController（走 ChatService.StreamWithEventsAsync 路径）。</summary>
    private static SessionController CreateController(IReadOnlyList<ChatStreamEvent> events, RecordingSubagentConsumer consumer) {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var chatService = CreateMockChatService(events);
        return new SessionController(chatService, consumer, new TurnDiffService(), "test-session", sp);
    }

    /// <summary>AgentStarted 事件 — 路由 OnSubagentStarted（id/名称/描述全字段）。</summary>
    [Fact]
    public async Task AgentStartedEvent_RoutesToOnSubagentStarted()
    {
        var consumer = new RecordingSubagentConsumer();
        var controller = CreateController(
        [
            ChatStreamEvent.AgentStarted("agent-1", "explore", "搜索渲染链", "researcher"),
            ChatStreamEvent.Done(),
        ], consumer);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        var started = Assert.Single(consumer.SubagentStarts);
        Assert.Equal("agent-1", started.SubagentId);
        Assert.Equal("explore", started.AgentType);
        Assert.Equal("搜索渲染链", started.Description);
    }

    /// <summary>AgentFinished 事件 — 路由 OnSubagentFinished（id/成败）。</summary>
    [Fact]
    public async Task AgentFinishedEvent_RoutesToOnSubagentFinished()
    {
        var consumer = new RecordingSubagentConsumer();
        var controller = CreateController(
        [
            ChatStreamEvent.AgentStarted("agent-1", "explore", "搜索"),
            new ChatStreamEvent { Type = ChatStreamEventType.AgentFinished, AgentId = "agent-1", AgentSuccess = false, Usage = new TokenUsage(300, 50) },
            ChatStreamEvent.Done(),
        ], consumer);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded, $"回合失败: {result.ErrorMessage ?? "(无)"}");
        var finished = Assert.Single(consumer.SubagentFinishes);
        Assert.Equal("agent-1", finished.SubagentId);
        Assert.True(finished.IsError);
        Assert.Equal(350, finished.TokenTotal);
    }

    /// <summary>带 AgentId 的工具事件 — 路由 OnSubagentToolUse 且不走普通 OnToolStart/OnToolEnd（分流）。</summary>
    [Fact]
    public async Task SubagentToolEvent_RoutesToOnSubagentToolUse_NotPlainToolCallbacks()
    {
        var consumer = new RecordingSubagentConsumer();
        var controller = CreateController(
        [
            ChatStreamEvent.AgentStarted("agent-1", "explore", "搜索"),
            new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, AgentId = "agent-1", ToolName = "Read", ToolArguments = "file.cs" },
            new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, AgentId = "agent-1", ToolName = "Read", ToolResultText = "内容…" },
            ChatStreamEvent.Done(),
        ], consumer);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal(2, consumer.SubagentToolUses.Count);
        Assert.All(consumer.SubagentToolUses, use => Assert.Equal("agent-1", use.SubagentId));
        Assert.Equal("Read", consumer.SubagentToolUses[0].ToolName);
        Assert.Equal("file.cs", consumer.SubagentToolUses[0].Summary);
        Assert.Equal("Read", consumer.SubagentToolUses[1].ToolName);
        Assert.Equal("内容…", consumer.SubagentToolUses[1].Summary);
        Assert.Empty(consumer.ToolStarts);
        Assert.Empty(consumer.ToolEnds);
    }

    /// <summary>普通工具事件（AgentId null）— 走既有 OnToolStart/OnToolEnd 零回归。</summary>
    [Fact]
    public async Task PlainToolEvent_StillRoutesToPlainToolCallbacks()
    {
        var consumer = new RecordingSubagentConsumer();
        var controller = CreateController(
        [
            ChatStreamEvent.ToolStart("Read", "call-1", "file.cs"),
            ChatStreamEvent.ToolEnd("Read", "内容…", "call-1"),
            ChatStreamEvent.Done(),
        ], consumer);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Empty(consumer.SubagentStarts);
        Assert.Empty(consumer.SubagentToolUses);
        var start = Assert.Single(consumer.ToolStarts);
        Assert.Equal("Read", start.toolName);
        var end = Assert.Single(consumer.ToolEnds);
        Assert.Equal("Read", end.toolName);
    }

    /// <summary>混合流 — 子代理事件与主对话事件并存时各自正确分流（回合收尾不受影响）。</summary>
    [Fact]
    public async Task MixedStream_RoutesEachEventCorrectly()
    {
        var consumer = new RecordingSubagentConsumer();
        var controller = CreateController(
        [
            ChatStreamEvent.Text("主回复前段"),
            ChatStreamEvent.AgentStarted("agent-1", "explore", "搜索"),
            new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, AgentId = "agent-1", ToolName = "Grep", ToolResultText = "命中" },
            new ChatStreamEvent { Type = ChatStreamEventType.AgentFinished, AgentId = "agent-1", AgentSuccess = true },
            ChatStreamEvent.Text("主回复后段"),
            ChatStreamEvent.Done(null, "test-model"),
        ], consumer);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal("主回复前段主回复后段", result.Response);
        Assert.Single(consumer.SubagentStarts);
        Assert.Single(consumer.SubagentToolUses);
        var finished = Assert.Single(consumer.SubagentFinishes);
        Assert.False(finished.IsError);
        Assert.Equal(2, consumer.TextEvents.Count);
        var done = Assert.Single(consumer.DoneEvents);
        Assert.Equal("test-model", done.modelId);
    }

    // ---- 批2 L1：主代理路径 SubAgentEventChannel 直投（DSG041） ----

    /// <summary>主代理路径通道事件直投 — QueryEngine 工具执行段 Emit 到通道（AsyncLocal 作用域内），
    /// SessionController chunk 循环排空后经 ProcessEvent 分流到三子代理回调（不经 chunk 转换层）。</summary>
    [Fact]
    public async Task MainAgentPath_ChannelEvents_DispatchToSubagentCallbacks()
    {
        var consumer = new RecordingSubagentConsumer();
        var controller = CreateMainAgentController(
        [
            new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = "主回复" },
            new QueryStreamChunk { Type = AgentStreamChunkType.Complete, ModelId = "test-model" },
        ],
        chunkIndex => EmitChannelEvents(chunkIndex), consumer);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded, $"回合失败: {result.ErrorMessage ?? "(无)"}");
        Assert.Equal("主回复", result.Response);
        var started = Assert.Single(consumer.SubagentStarts);
        Assert.Equal("sub-1", started.SubagentId);
        Assert.Equal("explore", started.AgentType);
        Assert.Equal("搜索渲染链", started.Description);
        Assert.Equal(2, consumer.SubagentToolUses.Count);
        Assert.All(consumer.SubagentToolUses, use => Assert.Equal("sub-1", use.SubagentId));
        var finished = Assert.Single(consumer.SubagentFinishes);
        Assert.Equal("sub-1", finished.SubagentId);
        Assert.False(finished.IsError);
    }

    /// <summary>主代理路径 — 子代理带 AgentId 工具事件经通道直投不产生普通 OnToolStart/OnToolEnd（分流一致）。</summary>
    [Fact]
    public async Task MainAgentPath_ChannelSubagentToolEvent_NotPlainToolCallbacks()
    {
        var consumer = new RecordingSubagentConsumer();
        var controller = CreateMainAgentController(
        [
            new QueryStreamChunk { Type = AgentStreamChunkType.Complete, ModelId = "test-model" },
        ],
        chunkIndex => {
            if (chunkIndex == 0) {
                SubAgentEventChannel.Current?.Emit(new ChatStreamEvent {
                    Type = ChatStreamEventType.ToolCallStart, AgentId = "sub-1", ToolName = "Read", ToolArguments = "file.cs" });
            }
        }, consumer);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        var use = Assert.Single(consumer.SubagentToolUses);
        Assert.Equal("Read", use.ToolName);
        Assert.Equal("file.cs", use.Summary);
        Assert.Empty(consumer.ToolStarts);
        Assert.Empty(consumer.ToolEnds);
    }

    /// <summary>构造主代理路径 controller（mock QueryEngine 喂 chunk 流，每 chunk yield 前回调发射通道事件）。</summary>
    private static SessionController CreateMainAgentController(
        IReadOnlyList<QueryStreamChunk> chunks,
        Action<int> onBeforeYield,
        RecordingSubagentConsumer consumer) {
        var queryEngine = CreateMockQueryEngine(chunks, onBeforeYield);
        var agent = new AgentBase(task: string.Empty, options: null, queryEngine: queryEngine, logger: null, name: "main", role: AgentRole.Coordinator);
        var sp = new ServiceCollection().BuildServiceProvider();
        var chatService = CreateMockChatService(Array.Empty<ChatStreamEvent>());
        return new SessionController(chatService, consumer, new TurnDiffService(), "test-session", sp, mainAgent: agent);
    }

    /// <summary>mock QueryEngine — chunk 流的 enumerator 在 yield 前回调（模拟工具执行段发射子代理事件到通道）。</summary>
    private static IQueryEngine CreateMockQueryEngine(IReadOnlyList<QueryStreamChunk> chunks, Action<int> onBeforeYield) {
        var mock = new Mock<IQueryEngine>();
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, CancellationToken ct) => EmitChunksWithEventsAsync(chunks, onBeforeYield, ct));
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, QueryOptions? opts, CancellationToken ct) => EmitChunksWithEventsAsync(chunks, onBeforeYield, ct));
        return mock.Object;
    }

    /// <summary>按 chunk 序号发射完整子代理事件序列（Started → 工具×2 → Finished）。</summary>
    private static void EmitChannelEvents(int chunkIndex) {
        var channel = SubAgentEventChannel.Current;
        if (channel is null || chunkIndex != 0) {
            return;
        }

        channel.Emit(ChatStreamEvent.AgentStarted("sub-1", "explore", "搜索渲染链"));
        channel.Emit(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallStart, AgentId = "sub-1", ToolName = "Grep", ToolArguments = "pattern" });
        channel.Emit(new ChatStreamEvent { Type = ChatStreamEventType.ToolCallEnd, AgentId = "sub-1", ToolName = "Grep", ToolResultText = "命中" });
        channel.Emit(ChatStreamEvent.AgentFinished("sub-1", success: true));
    }

    /// <summary>chunk 流 + yield 前回调（AsyncLocal 作用域在 enumerator 恢复时继承调用方作用域）。</summary>
    private static async IAsyncEnumerable<QueryStreamChunk> EmitChunksWithEventsAsync(
        IReadOnlyList<QueryStreamChunk> chunks,
        Action<int> onBeforeYield,
        [EnumeratorCancellation] CancellationToken ct) {
        for (var i = 0; i < chunks.Count; i++) {
            ct.ThrowIfCancellationRequested();
            onBeforeYield(i);
            yield return chunks[i];
        }
    }

    private static IChatService CreateMockChatService(IReadOnlyList<ChatStreamEvent> events) {
        var mock = new Mock<IChatService>();
        mock.Setup(s => s.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string input, CancellationToken ct) => EmitEventsAsync(events, ct));
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

    /// <summary>记录子代理 + 普通事件的 fake 消费者（对齐 RecordingEventConsumer 先例 + 三子代理回调）。</summary>
    private sealed class RecordingSubagentConsumer : IEventConsumer {
        public List<string> TextEvents { get; } = [];
        public List<(string toolName, string? toolCallId, string? arguments)> ToolStarts { get; } = [];
        public List<(string toolName, string? resultText, string? toolCallId, bool isError)> ToolEnds { get; } = [];
        public List<(TokenUsage? usage, string? modelId)> DoneEvents { get; } = [];
        public List<(string SubagentId, string AgentType, string Description)> SubagentStarts { get; } = [];
        public List<(string SubagentId, string ToolName, string Summary, int TokenGain)> SubagentToolUses { get; } = [];
        public List<(string SubagentId, bool IsError, int TokenTotal)> SubagentFinishes { get; } = [];

        public void OnText(string content) => TextEvents.Add(content);
        public void OnThinking(string thinking) { }
        public void OnToolStart(string toolName, string? toolCallId, string? arguments) => ToolStarts.Add((toolName, toolCallId, arguments));
        public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) => ToolEnds.Add((toolName, resultText, toolCallId, isError));
        public void OnToolProgress(string toolName, string progressType, string? progressMessage) { }
        public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) { }
        public void OnTimingSummary(string summary) { }
        public void OnDone(TokenUsage? usage, string? modelId) => DoneEvents.Add((usage, modelId));
        public void OnSubagentStarted(string subagentId, string agentType, string description) => SubagentStarts.Add((subagentId, agentType, description));
        public void OnSubagentToolUse(string subagentId, string toolName, string summary, int tokenGain) => SubagentToolUses.Add((subagentId, toolName, summary, tokenGain));
        public void OnSubagentFinished(string subagentId, bool isError, int tokenTotal) => SubagentFinishes.Add((subagentId, isError, tokenTotal));
    }
}
