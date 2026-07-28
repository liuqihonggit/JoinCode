namespace Core.Context;

public sealed class QueryLoopMiddlewareTests
{
    [Fact]
    public async Task MultiToolExecution_CancelledDuringSecondTool_WritesAbortedResultsForRemainingTools()
    {
        var toolCalls = new List<ToolCallEntry>
        {
            new() { Id = "call_001", Name = "Read", Arguments = "{\"file_path\":\"a.txt\"}" },
            new() { Id = "call_002", Name = "Read", Arguments = "{\"file_path\":\"b.txt\"}" },
            new() { Id = "call_003", Name = "Read", Arguments = "{\"file_path\":\"c.txt\"}" },
        };

        var cts = new CancellationTokenSource();
        var (contextManager, toolResultsAdded) = CreateContextManager();

        var toolOrchestrator = new Mock<IChatToolOrchestrator>();
        toolOrchestrator.Setup(t => t.ExecuteToolCallAsync("Read", "call_001", It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "content of a.txt", IsError = false });
        toolOrchestrator.Setup(t => t.ExecuteToolCallAsync("Read", "call_002", It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, Dictionary<string, JsonElement>?, CancellationToken>((_, _, _, _) => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var middleware = CreateMiddleware(contextManager, toolOrchestrator, toolCalls);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in middleware.InvokeAsync(
                CreateContext(), (ctx, ct) => AsyncEnumerableEmpty<ChatStreamEvent>(), cts.Token))
            { }
        }).ConfigureAwait(true);

        toolResultsAdded.Should().ContainSingle(r => r.ToolCallId == "call_001" && r.Content == "content of a.txt");
        toolResultsAdded.Should().Contain(r => r.ToolCallId == "call_002" && r.Content == "(aborted)");
        toolResultsAdded.Should().Contain(r => r.ToolCallId == "call_003" && r.Content == "(aborted)");
    }

    [Fact]
    public async Task MultiToolExecution_CancelledDuringResultPersistence_WritesAbortedResultsForCurrentAndRemaining()
    {
        var toolCalls = new List<ToolCallEntry>
        {
            new() { Id = "call_001", Name = "Read", Arguments = "{\"file_path\":\"a.txt\"}" },
            new() { Id = "call_002", Name = "Read", Arguments = "{\"file_path\":\"b.txt\"}" },
        };

        var cts = new CancellationTokenSource();
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantToolCallMessageAsync(
                It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolResultsAdded = new List<(string Content, string? ToolCallId, string ToolName)>();
        var callIndex = 0;
        contextManager.Setup(c => c.AddToolResultMessageAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>(), It.IsAny<IReadOnlyList<ToolContent>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyDictionary<string, JsonElement>, IReadOnlyList<ToolContent>?, CancellationToken>((content, metadata, _, ct) =>
            {
                callIndex++;
                var id = metadata.TryGetValue(MessageMetadataKeyConstants.ToolCallId, out var idElem)
                    ? idElem.GetString() : null;
                var name = metadata.TryGetValue(MessageMetadataKeyConstants.ToolName, out var nameElem)
                    ? nameElem.GetString() : "";
                toolResultsAdded.Add((content, id, name ?? ""));

                if (callIndex == 1)
                {
                    cts.Cancel();
                    throw new OperationCanceledException(cts.Token);
                }
            })
            .Returns(Task.CompletedTask);

        var toolOrchestrator = new Mock<IChatToolOrchestrator>();
        toolOrchestrator.Setup(t => t.ExecuteToolCallAsync("Read", "call_001", It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "content of a.txt", IsError = false });
        toolOrchestrator.Setup(t => t.ExecuteToolCallAsync("Read", "call_002", It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "content of b.txt", IsError = false });

        var middleware = CreateMiddleware(contextManager, toolOrchestrator, toolCalls);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in middleware.InvokeAsync(
                CreateContext(), (ctx, ct) => AsyncEnumerableEmpty<ChatStreamEvent>(), cts.Token))
            { }
        }).ConfigureAwait(true);

        toolResultsAdded.Should().Contain(r => r.ToolCallId == "call_001" && r.Content == "content of a.txt");
        toolResultsAdded.Should().Contain(r => r.ToolCallId == "call_002" && r.Content == "(aborted)");
    }

    [Fact]
    public async Task SingleToolExecution_NoCancellation_CompletesNormally()
    {
        var toolCalls = new List<ToolCallEntry>
        {
            new() { Id = "call_001", Name = "Read", Arguments = "{\"file_path\":\"a.txt\"}" },
        };

        var (contextManager, toolResultsAdded) = CreateContextManager();

        var toolOrchestrator = new Mock<IChatToolOrchestrator>();
        toolOrchestrator.Setup(t => t.ExecuteToolCallAsync("Read", "call_001", It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "content of a.txt", IsError = false });

        var middleware = CreateMiddleware(contextManager, toolOrchestrator, toolCalls);

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in middleware.InvokeAsync(
            CreateContext(), (ctx, ct) => AsyncEnumerableEmpty<ChatStreamEvent>(), CancellationToken.None))
        {
            events.Add(evt);
        }

        toolResultsAdded.Should().ContainSingle(r => r.ToolCallId == "call_001" && r.Content == "content of a.txt");
        toolResultsAdded.Should().NotContain(r => r.Content == "(aborted)");
    }

    private static (Mock<IChatContextManager> Mock, List<(string Content, string? ToolCallId, string ToolName)> Results) CreateContextManager()
    {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddAssistantToolCallMessageAsync(
                It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.SyncDiscoveredToolsFromHistoryAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolResultsAdded = new List<(string Content, string? ToolCallId, string ToolName)>();
        contextManager.Setup(c => c.AddToolResultMessageAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>(), It.IsAny<IReadOnlyList<ToolContent>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyDictionary<string, JsonElement>, IReadOnlyList<ToolContent>?, CancellationToken>((content, metadata, _, _) =>
            {
                var id = metadata.TryGetValue(MessageMetadataKeyConstants.ToolCallId, out var idElem)
                    ? idElem.GetString() : null;
                var name = metadata.TryGetValue(MessageMetadataKeyConstants.ToolName, out var nameElem)
                    ? nameElem.GetString() : "";
                toolResultsAdded.Add((content, id, name ?? ""));
            })
            .Returns(Task.CompletedTask);

        return (contextManager, toolResultsAdded);
    }

    /// <summary>
    /// 创建中间件 — 使用 Mock ILLMInvocationHandler 模拟工具调用场景，
    /// 真实 ToolExecutionHandler 包装 Mock IChatToolOrchestrator
    /// </summary>
    private static QueryLoopMiddleware CreateMiddleware(
        Mock<IChatContextManager> contextManager,
        Mock<IChatToolOrchestrator> toolOrchestrator,
        List<ToolCallEntry> toolCalls)
    {
        // Mock LLM handler — 第一次调用填充工具调用，第二次调用返回空（结束循环）
        var llmCallCount = 0;
        var llmHandler = new Mock<ILLMInvocationHandler>();
        llmHandler.Setup(h => h.InvokeLLMAsync(
            It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<ChatMiddlewareContext>(),
            It.IsAny<int>(), It.IsAny<IterationState>(), It.IsAny<CancellationToken>()))
        .Callback<MessageList, ChatOptions?, ChatMiddlewareContext, int, IterationState, CancellationToken>(
            (_, _, _, _, state, _) =>
            {
                llmCallCount++;
                if (llmCallCount <= 1)
                {
                    state.ToolCallName = "Read";
                    state.ToolCalls.AddRange(toolCalls);
                }
            })
        .Returns(AsyncEnumerableEmpty<ChatStreamEvent>());

        // 真实 ToolExecutionHandler 包装 Mock orchestrator — 保留异常处理和结果持久化逻辑
        var toolHandler = new ToolExecutionHandler(toolOrchestrator.Object, contextManager.Object);
        var notificationHandler = new BackgroundNotificationHandler(contextManager.Object);
        var telemetryRecorder = new TelemetryRecorder();

        return new QueryLoopMiddleware(
            notificationHandler,
            llmHandler.Object,
            toolHandler,
            telemetryRecorder,
            contextManager.Object);
    }

    private static ChatMiddlewareContext CreateContext() => new()
    {
        Message = "test",
        ToolUseContext = new ToolUseContext()
    };

    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(true);
        yield break;
    }
}
