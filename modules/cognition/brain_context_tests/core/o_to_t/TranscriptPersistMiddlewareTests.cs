namespace Brain.Tests.Context;

/// <summary>
/// TranscriptPersistMiddleware 测试 — 对话流结束后把 ChatHistory 快照差量增量写入 transcript。
/// 回归背景：transcript JSONL 落盘此前由三端各自手写（CLI=CliSession 手动、GUI=GuiSessionStore
/// 全量覆盖、TUI=TuiSessionStore），违反单一实现原则；下沉为引擎管道中间件后三端自动获得。
/// </summary>
public sealed class TranscriptPersistMiddlewareTests {
    private static (TranscriptPersistMiddleware Middleware, Mock<ITranscriptService> Transcript, Mock<IChatContextManager> CtxMgr) Create(
        IReadOnlyList<ApiMessage>? messages = null) {
        var transcript = new Mock<ITranscriptService>();
        var ctxMgr = new Mock<IChatContextManager>();
        var messages1 = messages ?? [];
        ctxMgr.SetupGet(c => c.SessionId).Returns("20260822-1200-proj-main");
        ctxMgr.Setup(c => c.GetMessageListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new MessageList(messages1));
        var mw = new TranscriptPersistMiddleware(
            transcript.Object, ctxMgr.Object, NullLogger<TranscriptPersistMiddleware>.Instance);
        return (mw, transcript, ctxMgr);
    }

    private static async Task<List<ChatStreamEvent>> RunAsync(TranscriptPersistMiddleware mw, bool dryRun = false) {
        var context = new ChatMiddlewareContext {
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
        [EnumeratorCancellation] CancellationToken ct = default) {
        yield break;
    }

    [Fact]
    public void OnError_Is_Continue() {
        var (mw, _, _) = Create();
        mw.OnError.Should().Be(ErrorBehavior.Continue, "落盘失败不得中断对话");
    }

    [Fact]
    public async Task InvokeAsync_WithNewMessages_AppendsDeltaEntries() {
        // TranscriptPersist 在 PreChat 之前：startSnapshot 不含本轮 user，结束 snapshot 含 user+assistant
        var startMessages = new List<ApiMessage>
        {
            new(MessageRole.User, "旧问题"),
            new(MessageRole.Assistant, "旧回答"),
        };
        var endMessages = new List<ApiMessage>
        {
            new(MessageRole.User, "旧问题"),
            new(MessageRole.Assistant, "旧回答"),
            new(MessageRole.User, "新问题"),
            new(MessageRole.Assistant, "新回答"),
        };
        var (mw, transcript, ctxMgr) = Create();
        ctxMgr.SetupSequence(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList(startMessages))
            .ReturnsAsync(new MessageList(endMessages));

        await RunAsync(mw);

        transcript.Verify(t => t.AppendEntriesAsync(
            "20260822-1200-proj-main",
            It.Is<IReadOnlyList<TranscriptEntry>>(entries => entries.Count == 2
                && entries[0].Content == "新问题"
                && entries[1].Content == "新回答"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NoDelta_DoesNotWrite() {
        var messages = new List<ApiMessage> { new(MessageRole.User, "只有旧的") };
        var (mw, transcript, ctxMgr) = Create();
        ctxMgr.SetupSequence(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList(messages))
            .ReturnsAsync(new MessageList(messages));

        await RunAsync(mw);

        transcript.Verify(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()), Times.Never,
            "无差量不产生写入");
    }

    [Fact]
    public async Task InvokeAsync_DryRun_DoesNotWrite() {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "a"),
            new(MessageRole.Assistant, "b"),
        };
        var (mw, transcript, _) = Create(messages);

        await RunAsync(mw, dryRun: true);

        transcript.Verify(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()), Times.Never,
            "干运行不落盘（对齐 SaveContextMiddleware 语义）");
    }

    [Fact]
    public async Task InvokeAsync_TranscriptThrows_EventsStillFlow() {
        var startMessages = new List<ApiMessage>();
        var endMessages = new List<ApiMessage>
        {
            new(MessageRole.User, "q"),
            new(MessageRole.Assistant, "r"),
        };
        var (mw, transcript, ctxMgr) = Create();
        ctxMgr.SetupSequence(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList(startMessages))
            .ReturnsAsync(new MessageList(endMessages));
        transcript.Setup(t => t.AppendEntriesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        var events = await RunAsync(mw);

        events.Should().NotBeNull("落盘异常不得吞掉下游事件流（OnError=Continue 契约）");
    }

    /// <summary>流式正文累积超过节流阈值即写旁路快照（ADR 0166 — 打断/崩溃只丢节流窗口内的内容）。</summary>
    [Fact]
    public async Task InvokeAsync_WithContentDeltas_WritesStreamingSnapshots() {
        var (mw, transcript, ctxMgr) = Create();
        ctxMgr.SetupSequence(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList(new List<ApiMessage>()))
            .ReturnsAsync(new MessageList(new List<ApiMessage> { new(MessageRole.Assistant, "答复") }));

        await RunWithAsync(mw, DeltasAsync(new string('x', 600)));

        transcript.Verify(t => t.AppendStreamingSnapshotAsync(
            "20260822-1200-proj-main",
            It.Is<string>(content => content.Length >= 512),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "累积正文达到 512 字符阈值必须落旁路，否则打断后整段丢失");
    }

    /// <summary>回合正常结束并落盘后清理旁路（否则下次加载会收编成重复残稿）。</summary>
    [Fact]
    public async Task InvokeAsync_AfterTurnPersisted_ClearsStreaming() {
        var (mw, transcript, ctxMgr) = Create();
        ctxMgr.SetupSequence(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList(new List<ApiMessage>()))
            .ReturnsAsync(new MessageList(new List<ApiMessage> { new(MessageRole.Assistant, "答复") }));

        await RunWithAsync(mw, DeltasAsync(new string('y', 600)));

        transcript.Verify(t => t.ClearStreamingAsync("20260822-1200-proj-main", It.IsAny<CancellationToken>()), Times.Once,
            "正常落盘后旁路必须清理");
    }

    /// <summary>流中断（取消/异常）时**不清理**旁路 — 残稿要留在磁盘上等下次加载收编。</summary>
    [Fact]
    public async Task InvokeAsync_WhenStreamThrows_KeepsStreaming() {
        var (mw, transcript, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunWithAsync(mw, ThrowingAsync(new string('z', 600)))).ConfigureAwait(true);

        transcript.Verify(t => t.AppendStreamingSnapshotAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "异常前已生成的内容仍要落旁路");
        transcript.Verify(t => t.ClearStreamingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "中断路径不得清理旁路，否则残稿永久丢失");
    }

    /// <summary>以指定事件流驱动中间件（收集下游收到的事件）。</summary>
    private static async Task<List<ChatStreamEvent>> RunWithAsync(
        TranscriptPersistMiddleware mw, IAsyncEnumerable<ChatStreamEvent> stream, bool dryRun = false) {
        var context = new ChatMiddlewareContext {
            Message = "hi",
            IsDryRun = dryRun,
            ToolUseContext = new ToolUseContext(),
        };
        var events = new List<ChatStreamEvent>();
        await foreach (var evt in mw.InvokeAsync(context, (_, _) => stream, CancellationToken.None).ConfigureAwait(true))
            events.Add(evt);
        return events;
    }

    /// <summary>ADR 0167 — 回合进行中发生 /clear（换 sessionId），本回合 transcript 仍必须落**旧**代。
    /// 现状（红）：落盘时实时读 <c>_contextManager.SessionId</c>，会写到新代目录。</summary>
    [Fact]
    public async Task InvokeAsync_SessionSwitchedMidTurn_StillWritesToOriginalSession() {
        var session = "s-old";
        var (mw, transcript, ctxMgr) = Create();
        ctxMgr.SetupGet(c => c.SessionId).Returns(() => session);
        ctxMgr.SetupSequence(c => c.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList(new List<ApiMessage> { new(MessageRole.User, "旧问题") }))
            .ReturnsAsync(new MessageList(new List<ApiMessage> {
                new(MessageRole.User, "旧问题"),
                new(MessageRole.User, "本回合的问题"),
                new(MessageRole.Assistant, "本回合的回答"),
            }));

        await RunWithAsync(mw, SwitchSessionAsync(() => session = "s-new")).ConfigureAwait(true);

        transcript.Verify(t => t.AppendEntriesAsync(
            "s-old",
            It.Is<IReadOnlyList<TranscriptEntry>>(entries => entries.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once,
            "回合出生在 s-old，/clear 之后也必须写 s-old（否则旧代记录不完整、新代被污染）");
        transcript.Verify(t => t.AppendEntriesAsync(
            "s-new", It.IsAny<IReadOnlyList<TranscriptEntry>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>下游开始即触发切换（模拟回合中途 /clear），随后正常消费空流。</summary>
    private static async IAsyncEnumerable<ChatStreamEvent> SwitchSessionAsync(
        Action onEnter, [EnumeratorCancellation] CancellationToken ct = default) {
        onEnter();
        await Task.CompletedTask.ConfigureAwait(true);
        yield break;
    }

    /// <summary>把文本切成分片 Content 事件（模拟流式正文）。</summary>
    private static async IAsyncEnumerable<ChatStreamEvent> DeltasAsync(string text) {
        for (var i = 0; i < text.Length; i += 100) {
            yield return new ChatStreamEvent {
                Type = ChatStreamEventType.Content,
                Content = text.Substring(i, Math.Min(100, text.Length - i)),
            };
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>先吐分片正文再抛异常（模拟流中断）。</summary>
    private static async IAsyncEnumerable<ChatStreamEvent> ThrowingAsync(string text) {
        await foreach (var evt in DeltasAsync(text).ConfigureAwait(true)) {
            yield return evt;
        }

        throw new InvalidOperationException("stream aborted");
    }
}