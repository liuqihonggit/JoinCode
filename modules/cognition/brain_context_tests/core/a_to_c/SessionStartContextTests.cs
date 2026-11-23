namespace Core.Tests.Context;

/// <summary>
/// TASK130-A6：SessionStart 上下文的两端。
/// 上半段验证 init 时写 store（<see cref="SessionStartHookMiddleware"/>），
/// 下半段验证每回合补写动态系统消息（<see cref="SessionStartContextMiddleware"/>）。
/// 之所以要两段：只写一次会被 system-prompt-mw 每回合的 ClearDynamicSystemMessagesAsync 清掉。
/// </summary>
public class SessionStartContextTests {
    private static readonly MiddlewareDelegate<PreprocessContext> NoopPreprocessNext = static (_, _) => Task.CompletedTask;
    private static readonly MiddlewareDelegate<ChatInitContext> NoopInitNext = static (_, _) => Task.CompletedTask;

    // ── 写入端：init ──────────────────────────────────────────────

    [Fact]
    public async Task InitHook_WithAdditionalContext_WritesStore() {
        var store = new SessionStartContextStore();
        var manager = new Mock<ISessionStartHookManager>();
        manager.Setup(m => m.OnSessionStartAsync(It.IsAny<SessionStartHookContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionStartHookResult { AdditionalContext = "本仓库禁止直接提交到 main" });
        await using var sut = new SessionStartHookMiddleware(manager.Object, store);

        await sut.InvokeAsync(CreateInitContext("session-a"), NoopInitNext, CancellationToken.None).ConfigureAwait(true);

        store.Get("session-a").Should().Be("本仓库禁止直接提交到 main");
    }

    [Fact]
    public async Task InitHook_WithoutContext_ClearsPreviousSession() {
        var store = new SessionStartContextStore();
        store.Set("session-a", "上一轮的上下文");
        var manager = new Mock<ISessionStartHookManager>();
        manager.Setup(m => m.OnSessionStartAsync(It.IsAny<SessionStartHookContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionStartHookResult());
        await using var sut = new SessionStartHookMiddleware(manager.Object, store);

        await sut.InvokeAsync(CreateInitContext("session-a"), NoopInitNext, CancellationToken.None).ConfigureAwait(true);

        store.Get("session-a").Should().BeNull("/clear 重跑 init 时旧上下文不许残留");
    }

    [Fact]
    public async Task InitHook_InitialUserMessage_NotStored() {
        var store = new SessionStartContextStore();
        var manager = new Mock<ISessionStartHookManager>();
        manager.Setup(m => m.OnSessionStartAsync(It.IsAny<SessionStartHookContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionStartHookResult { InitialUserMessage = "继续上次的重构" });
        await using var sut = new SessionStartHookMiddleware(manager.Object, store);

        await sut.InvokeAsync(CreateInitContext("session-a"), NoopInitNext, CancellationToken.None).ConfigureAwait(true);

        store.Get("session-a").Should().BeNull(
            "initialUserMessage 不消费：插入会凭空产生一个用户回合（TASK130 A6 判定）");
    }

    [Fact]
    public async Task InitHook_Blocked_StillCallsNext() {
        var store = new SessionStartContextStore();
        var manager = new Mock<ISessionStartHookManager>();
        manager.Setup(m => m.OnSessionStartAsync(It.IsAny<SessionStartHookContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionStartHookResult { ShouldProceed = false, Message = "别进来" });
        await using var sut = new SessionStartHookMiddleware(manager.Object, store);
        var nextCalled = false;

        await sut.InvokeAsync(
            CreateInitContext("session-a"),
            (_, _) => {
                nextCalled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue("OnError=Continue，阻断只记日志不改管道行为（既有语义）");
        store.Get("session-a").Should().BeNull();
    }

    // ── 消费端：每回合 ────────────────────────────────────────────

    [Fact]
    public async Task Preprocess_SameSession_InjectsSingleDynamicMessage() {
        var store = new SessionStartContextStore();
        store.Set("session-a", "本仓库禁止直接提交到 main");
        var context = ChatSpy("session-a", out var captured);
        await using var sut = new SessionStartContextMiddleware(context.Object, store);

        await sut.InvokeAsync(new PreprocessContext { Message = "hi" }, NoopPreprocessNext, CancellationToken.None).ConfigureAwait(true);

        captured.Should().ContainSingle();
        captured[0].Should().Be("本仓库禁止直接提交到 main");
    }

    [Fact]
    public async Task Preprocess_EmptyStore_InjectsNothing() {
        var store = new SessionStartContextStore();
        var context = ChatSpy("session-a", out var captured);
        await using var sut = new SessionStartContextMiddleware(context.Object, store);

        await sut.InvokeAsync(new PreprocessContext { Message = "hi" }, NoopPreprocessNext, CancellationToken.None).ConfigureAwait(true);

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Preprocess_OtherSession_DoesNotLeak() {
        var store = new SessionStartContextStore();
        store.Set("session-a", "A 的上下文");
        var context = ChatSpy("session-b", out var captured);
        await using var sut = new SessionStartContextMiddleware(context.Object, store);

        await sut.InvokeAsync(new PreprocessContext { Message = "hi" }, NoopPreprocessNext, CancellationToken.None).ConfigureAwait(true);

        captured.Should().BeEmpty("/session 切走后不许把上一个会话的上下文带过去");
    }

    [Fact]
    public async Task Preprocess_WriteThrows_StillCallsNext() {
        var store = new SessionStartContextStore();
        store.Set("session-a", "x");
        var context = new Mock<IChatContextManager>();
        context.SetupGet(m => m.SessionId).Returns("session-a");
        context.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("写炸了"));
        await using var sut = new SessionStartContextMiddleware(context.Object, store);
        var nextCalled = false;

        await sut.InvokeAsync(
            new PreprocessContext { Message = "hi" },
            (_, _) => {
                nextCalled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue("上下文注入是锦上添花，不许拦住回合");
    }

    [Fact]
    public async Task Preprocess_MissingDependencies_SkipsGracefully() {
        await using var sut = new SessionStartContextMiddleware();
        var nextCalled = false;

        await sut.InvokeAsync(
            new PreprocessContext { Message = "hi" },
            (_, _) => {
                nextCalled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
    }

    private static ChatInitContext CreateInitContext(string sessionId) => new() {
        ToolUseContext = new ToolUseContext(),
        ContextManager = new Mock<IChatContextManager>().Object,
        SessionId = sessionId,
    };

    private static Mock<IChatContextManager> ChatSpy(string sessionId, out List<string> captured) {
        var list = new List<string>();
        captured = list;
        var mock = new Mock<IChatContextManager>();
        mock.SetupGet(m => m.SessionId).Returns(sessionId);
        mock.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((text, _) => list.Add(text))
            .Returns(Task.CompletedTask);
        return mock;
    }
}
