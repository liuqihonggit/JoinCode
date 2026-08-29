namespace Brain.Tests.Context;

/// <summary>
/// TranscriptPersistMiddleware 测试 — 对话流结束后把 ChatHistory 快照差量增量写入 transcript。
/// 回归背景：transcript JSONL 落盘此前由三端各自手写（CLI=CliSession 手动、GUI=GuiSessionStore
/// 全量覆盖、TUI=TuiSessionStore），违反单一实现原则；下沉为引擎管道中间件后三端自动获得。
/// </summary>
public sealed class TranscriptPersistMiddlewareTests
{
    private static (TranscriptPersistMiddleware Middleware, Mock<ITranscriptService> Transcript, Mock<IChatContextManager> CtxMgr) Create(
        IReadOnlyList<ApiMessage>? messages = null)
    {
        var transcript = new Mock<ITranscriptService>();
        var ctxMgr = new Mock<IChatContextManager>();
        var messages1 = messages ?? [];
        ctxMgr.SetupGet(c => c.SessionId).Returns("20260822-1200-proj-main");
        ctxMgr.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new MessageList(messages1));
        var mw = new TranscriptPersistMiddleware(
            transcript.Object, ctxMgr.Object, NullLogger<TranscriptPersistMiddleware>.Instance);
        return (mw, transcript, ctxMgr);
    }

    private static async Task<List<ChatStreamEvent>> RunAsync(TranscriptPersistMiddleware mw, bool dryRun = false)
    {
        var context = new ChatMiddlewareContext
        {
            Message = "hi",
            IsDryRun = dryRun,
            ToolUseContext = new ToolUseContext(),
        };
        var events = new List<ChatStreamEvent>();
        await foreach (var evt in mw.InvokeAsync(context, EmptyStreamAsync, CancellationToken.None).ConfigureAwait(true))
            events.Add(evt);
        return events;
    }

    private static async IAsyncEnumerable<ChatStreamEvent> EmptyStreamAsync(
        ChatMiddlewareContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield break;
    }

    [Fact]
    public void OnError_Is_Continue()
    {
        var (mw, _, _) = Create();
        mw.OnError.Should().Be(ErrorBehavior.Continue, "落盘失败不得中断对话");
    }

    [Fact]
    public async Task InvokeAsync_WithNewMessages_AppendsDeltaEntries()
    {
        // 轮次开始时 2 条，结束时 4 条 → 差量 2 条写盘
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "旧问题"),
            new(MessageRole.Assistant, "旧回答"),
            new(MessageRole.User, "新问题"),
            new(MessageRole.Assistant, "新回答"),
        };
        var (mw, transcript, ctxMgr) = Create(messages);
        ctxMgr.SetupSequence(c => c.CurrentMessageCount).Returns(2).Returns(4);

        await RunAsync(mw);

        transcript.Verify(t => t.AppendEntriesAsync(
            "20260822-1200-proj-main",
            It.Is<IReadOnlyList<TranscriptEntry>>(entries => entries.Count == 2
                && entries[0].Content == "新问题"
                && entries[1].Content == "新回答"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NoDelta_DoesNotWrite()
    {
        var messages = new List<ApiMessage> { new(MessageRole.User, "只有旧的") };
        var (mw, transcript, ctxMgr) = Create(messages);
        ctxMgr.SetupSequence(c => c.CurrentMessageCount).Returns(1).Returns(1);

        await RunAsync(mw);

        transcript.Verify(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()), Times.Never,
            "无差量不产生写入");
    }

    [Fact]
    public async Task InvokeAsync_DryRun_DoesNotWrite()
    {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "a"),
            new(MessageRole.Assistant, "b"),
        };
        var (mw, transcript, ctxMgr) = Create(messages);
        ctxMgr.SetupSequence(c => c.CurrentMessageCount).Returns(0).Returns(2);

        await RunAsync(mw, dryRun: true);

        transcript.Verify(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()), Times.Never,
            "干运行不落盘（对齐 SaveContextMiddleware 语义）");
    }

    [Fact]
    public async Task InvokeAsync_TranscriptThrows_EventsStillFlow()
    {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "q"),
            new(MessageRole.Assistant, "r"),
        };
        var (mw, transcript, ctxMgr) = Create(messages);
        ctxMgr.SetupSequence(c => c.CurrentMessageCount).Returns(0).Returns(2);
        transcript.Setup(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        var events = await RunAsync(mw);

        events.Should().NotBeNull("落盘异常不得吞掉下游事件流（OnError=Continue 契约）");
    }
}
