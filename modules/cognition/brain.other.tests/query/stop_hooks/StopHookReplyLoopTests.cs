namespace Core.Tests.StopHooks;

/// <summary>
/// TASK071 query2-③ — Stop hook 中间件组件测试（对齐 TS stopHooks.ts:257-263）：
/// ShouldStop=true（hook 请求阻断）→ 阻断消息进 context.StopHookBlockingMessages（核心循环据此回灌继续一轮）；
/// sessionId 从 QueryOptions.SessionId 透传（旧硬编码 "session-id" 致会话级钩子永不匹配）；
/// ShouldStop=false（Continue）→ 不产生阻断消息。
/// </summary>
public sealed class StopHookReplyLoopTests {
    /// <summary>可编程阻断序列 + 会话 ID 捕获的假 Stop hook 管理器。</summary>
    private sealed class FakeStopHookManager : IQueryStopHookManager {
        public List<string> SeenSessionIds { get; } = [];
        public Queue<StopHookResult> Results { get; } = new();

        public Task<StopHookResult> ExecuteStopHooksAsync(string sessionId, string reason, CancellationToken ct = default) {
            SeenSessionIds.Add(sessionId);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : StopHookResult.Continue());
        }

        public void RegisterStopHook(IQueryStopHook hook) { }
        public void UnregisterStopHook(string hookName) { }
    }

    private static QueryMiddlewareContext CreateContext(string? sessionId = null) => new() {
        UserInput = "test",
        ChatHistory = new MessageList(),
        Config = new QueryEngineConfig(),
        Kernel = new Mock<IChatClient>().Object,
        ToolRegistry = new Mock<IToolRegistry>().Object,
        Options = sessionId is null ? null : new QueryOptions { SessionId = sessionId },
    };

    private static async Task<(QueryMiddlewareContext Context, FakeStopHookManager Manager)> RunHookAsync(
        FakeStopHookManager manager, QueryMiddlewareContext context) {
        await using var middleware = new StopHookMiddleware(manager);
        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        foreach (var hook in context.OnCompleteHooks) {
            await hook(context, CancellationToken.None).ConfigureAwait(true);
        }

        return (context, manager);
    }

    [Fact]
    public async Task StopRequested_AddsBlockingMessage_WithSessionIdFromOptions() {
        var manager = new FakeStopHookManager();
        manager.Results.Enqueue(StopHookResult.Stop("任务未完成，请继续收尾"));

        var (context, seen) = await RunHookAsync(manager, CreateContext(sessionId: "my-session-42")).ConfigureAwait(true);

        context.StopHookBlockingMessages.Should().ContainSingle("hook 请求阻断应产生回灌消息");
        context.StopHookBlockingMessages[0].Should().Be("任务未完成，请继续收尾");
        seen.SeenSessionIds.Should().Contain("my-session-42", "sessionId 应从 QueryOptions 透传（旧硬编码修复）");
    }

    [Fact]
    public async Task StopRequested_WithoutMessage_UsesFallbackText() {
        var manager = new FakeStopHookManager();
        manager.Results.Enqueue(StopHookResult.Stop());

        var (context, _) = await RunHookAsync(manager, CreateContext()).ConfigureAwait(true);

        context.StopHookBlockingMessages.Should().ContainSingle();
        context.StopHookBlockingMessages[0].Should().Contain("Stop hook");
    }

    [Fact]
    public async Task HookContinues_NoBlockingMessage() {
        var manager = new FakeStopHookManager();
        manager.Results.Enqueue(StopHookResult.Continue());

        var (context, _) = await RunHookAsync(manager, CreateContext()).ConfigureAwait(true);

        context.StopHookBlockingMessages.Should().BeEmpty("Continue 语义不应产生回灌消息");
    }
}
