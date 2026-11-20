// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Context;

/// <summary>
/// TASK102 轮次闸门 — QueryLoopMiddleware 收尾拦截测试（与 QueryEngine 共用 RoundGatePolicy）。
/// </summary>
public sealed class RoundGateEnforcementTests {
    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>([EnumeratorCancellation] CancellationToken ct = default) {
        await Task.Yield();
        yield break;
    }

    [Fact]
    public async Task StopWithoutEndChoice_InjectsBlockingMessage_ThenContinuesUntilEndChoice() {
        var systemMessages = new List<string>();
        var contextManager = CreateContextManager(systemMessages);

        var llmCallCount = 0;
        var llmHandler = new Mock<ILLMInvocationHandler>();
        llmHandler.Setup(h => h.InvokeLLMAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<ChatMiddlewareContext>(),
                It.IsAny<int>(), It.IsAny<IterationState>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions?, ChatMiddlewareContext, int, IterationState, bool, CancellationToken>(
                (_, _, _, _, state, _, _) => {
                    llmCallCount++;
                    if (llmCallCount == 1) {
                        state.FullResponse.Append("第一次想收尾");
                    } else if (llmCallCount == 2) {
                        state.ToolCallName = "round_gate";
                        state.ToolCalls.Add(new ToolCallEntry {
                            Id = "call-end",
                            Name = "round_gate",
                            Arguments = """{"choice":5}"""
                        });
                    } else {
                        state.FullResponse.Append("收尾总结");
                    }
                })
            .Returns(AsyncEnumerableEmpty<ChatStreamEvent>());

        var orchestrator = new Mock<IChatToolOrchestrator>();
        orchestrator.Setup(t => t.ExecuteToolCallAsync(
                "round_gate", "call-end", It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolCallResult { ResultText = "闸门已记录", IsError = false });

        await using var middleware = new QueryLoopMiddleware(
            new BackgroundNotificationHandler(contextManager.Object),
            llmHandler.Object,
            new ToolExecutionHandler(orchestrator.Object, contextManager.Object),
            new TelemetryRecorder(),
            contextManager.Object,
            new EmptyResponseTracker());

        var context = new ChatMiddlewareContext {
            Message = "test",
            ToolUseContext = new ToolUseContext(),
            RequireRoundGate = true
        };

        await foreach (var _ in middleware.InvokeAsync(
            context, (_, _) => AsyncEnumerableEmpty<ChatStreamEvent>(), CancellationToken.None)) { }

        llmCallCount.Should().Be(3, "第一次收尾被拦 → 第二次过闸门 → 第三次正常收尾");
        systemMessages.Should().ContainSingle(m => m.Contains("收尾被拦截"));
        orchestrator.Verify(t => t.ExecuteToolCallAsync(
            "round_gate", "call-end", It.IsAny<Dictionary<string, JsonElement>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GateDisabled_StopsImmediately() {
        var systemMessages = new List<string>();
        var contextManager = CreateContextManager(systemMessages);

        var llmCallCount = 0;
        var llmHandler = new Mock<ILLMInvocationHandler>();
        llmHandler.Setup(h => h.InvokeLLMAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<ChatMiddlewareContext>(),
                It.IsAny<int>(), It.IsAny<IterationState>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions?, ChatMiddlewareContext, int, IterationState, bool, CancellationToken>(
                (_, _, _, _, state, _, _) => {
                    llmCallCount++;
                    state.FullResponse.Append("普通回答");
                })
            .Returns(AsyncEnumerableEmpty<ChatStreamEvent>());

        await using var middleware = new QueryLoopMiddleware(
            new BackgroundNotificationHandler(contextManager.Object),
            llmHandler.Object,
            new ToolExecutionHandler(new Mock<IChatToolOrchestrator>().Object, contextManager.Object),
            new TelemetryRecorder(),
            contextManager.Object,
            new EmptyResponseTracker());

        var context = new ChatMiddlewareContext {
            Message = "test",
            ToolUseContext = new ToolUseContext(),
            RequireRoundGate = false
        };

        await foreach (var _ in middleware.InvokeAsync(
            context, (_, _) => AsyncEnumerableEmpty<ChatStreamEvent>(), CancellationToken.None)) { }

        llmCallCount.Should().Be(1);
        systemMessages.Should().NotContain(m => m.Contains("收尾被拦截"));
    }

    private static Mock<IChatContextManager> CreateContextManager(List<string> systemMessages) {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        contextManager.Setup(c => c.AddSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((content, _) => systemMessages.Add(content))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddAssistantMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddAssistantToolCallMessageAsync(
                It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.AddToolResultMessageAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>(),
                It.IsAny<IReadOnlyList<ToolContent>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        contextManager.Setup(c => c.SyncDiscoveredToolsFromHistoryAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return contextManager;
    }
}
