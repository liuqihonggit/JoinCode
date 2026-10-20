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
            new ChatStreamEvent { Type = ChatStreamEventType.AgentFinished, AgentId = "agent-1", AgentSuccess = false },
            ChatStreamEvent.Done(),
        ], consumer);

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded, $"回合失败: {result.ErrorMessage ?? "(无)"}");
        var finished = Assert.Single(consumer.SubagentFinishes);
        Assert.Equal("agent-1", finished.SubagentId);
        Assert.True(finished.IsError);
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
        public List<(string SubagentId, bool IsError)> SubagentFinishes { get; } = [];

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
        public void OnSubagentFinished(string subagentId, bool isError) => SubagentFinishes.Add((subagentId, isError));
    }
}
