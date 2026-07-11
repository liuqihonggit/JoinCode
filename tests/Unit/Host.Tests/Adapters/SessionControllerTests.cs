namespace Host.Tests.Adapters;

public sealed class SessionControllerTests
{
    [Fact]
    public async Task StreamResponseAsync_Success_ReturnsSuccessResult()
    {
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
    public async Task StreamResponseAsync_ToolEvents_DispatchedToConsumer()
    {
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
    public async Task StreamResponseAsync_Thinking_DispatchedToConsumer()
    {
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
    public async Task StreamResponseAsync_Timeout_ReturnsTimeoutResult()
    {
        var chatService = CreateMockChatService(
            _ => new OperationCanceledException());
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task StreamResponseAsync_Error_ReturnsErrorResult()
    {
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
    public async Task StreamResponseAsync_LoopDetected_DispatchedToConsumer()
    {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.LoopDetected(3, 0, "repeated pattern"),
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
    public async Task StreamResponseAsync_ToolProgress_DispatchedToConsumer()
    {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.ToolProgress("web_search", "query_update", "searching..."),
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
    public async Task StreamResponseAsync_TimingSummary_DispatchedToConsumer()
    {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.TimingSummary("TTFT: 1.2s, Total: 3.5s"),
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
    public async Task StreamResponseAsync_Done_StoresThinkingContent()
    {
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
    public async Task StreamResponseAsync_EmptyEvents_ReturnsSuccessWithEmptyResponse()
    {
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Done()
        };
        var chatService = CreateMockChatService(events);
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        var result = await controller.StreamResponseAsync("test", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Response);
    }

    [Fact]
    public void Stop_SetsIsRunningFalse()
    {
        var chatService = CreateMockChatService(Array.Empty<ChatStreamEvent>());
        var consumer = new RecordingEventConsumer();
        var turnDiffService = new TurnDiffService();
        var controller = new SessionController(chatService, consumer, turnDiffService, "test-session");

        Assert.True(controller.IsRunning);
        controller.Stop();
        Assert.False(controller.IsRunning);
    }

    private static IChatService CreateMockChatService(IReadOnlyList<ChatStreamEvent> events)
    {
        var mock = new Mock<IChatService>();
        mock.Setup(s => s.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string input, CancellationToken ct) => EmitEventsAsync(events, ct));
        return mock.Object;
    }

    private static IChatService CreateMockChatService(Func<string, Exception> throwFunc, int delayMs = 0)
    {
        var mock = new Mock<IChatService>();
        mock.Setup(s => s.StreamWithEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string input, CancellationToken ct) => ThrowEventsAsync(throwFunc, input, delayMs, ct));
        return mock.Object;
    }

    private static async IAsyncEnumerable<ChatStreamEvent> EmitEventsAsync(
        IReadOnlyList<ChatStreamEvent> events,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            yield return evt;
        }
    }

    private static async IAsyncEnumerable<ChatStreamEvent> ThrowEventsAsync(
        Func<string, Exception> throwFunc,
        string input,
        int delayMs,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(true);
        }
        throw throwFunc(input);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private sealed class RecordingEventConsumer : IEventConsumer
    {
        public List<string> TextEvents { get; } = [];
        public List<string> ThinkingEvents { get; } = [];
        public List<(string toolName, string? toolCallId, string? arguments)> ToolStarts { get; } = [];
        public List<(string toolName, string? resultText, string? toolCallId, bool isError)> ToolEnds { get; } = [];
        public List<(string toolName, string progressType, string? progressMessage)> ToolProgressEvents { get; } = [];
        public List<(int triggerCount, int loopStartIndex, string? repeatedPattern)> LoopDetectedEvents { get; } = [];
        public List<string> TimingSummaryEvents { get; } = [];
        public List<(TokenUsage? usage, string? modelId)> DoneEvents { get; } = [];

        public void OnText(string content) => TextEvents.Add(content);
        public void OnThinking(string thinking) => ThinkingEvents.Add(thinking);
        public void OnToolStart(string toolName, string? toolCallId, string? arguments) => ToolStarts.Add((toolName, toolCallId, arguments));
        public void OnToolEnd(string toolName, string? resultText, string? toolCallId, bool isError, StructuredPatchHunk[]? patch) => ToolEnds.Add((toolName, resultText, toolCallId, isError));
        public void OnToolProgress(string toolName, string progressType, string? progressMessage) => ToolProgressEvents.Add((toolName, progressType, progressMessage));
        public void OnLoopDetected(int triggerCount, int loopStartIndex, string? repeatedPattern) => LoopDetectedEvents.Add((triggerCount, loopStartIndex, repeatedPattern));
        public void OnTimingSummary(string summary) => TimingSummaryEvents.Add(summary);
        public void OnDone(TokenUsage? usage, string? modelId) => DoneEvents.Add((usage, modelId));
    }
}
