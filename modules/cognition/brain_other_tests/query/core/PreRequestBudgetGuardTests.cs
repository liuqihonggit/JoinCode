namespace Core.Tests.Query.Budget;

/// <summary>
/// ADR 0156 请求前上下文预算守卫 — query 循环版（对齐 chat 循环 GuardContextBudget）：
/// 每轮 LLM 请求前估算实时历史占比，≥0.7 预防性裁剪 / ≥1.0 紧急裁剪，避免 query 后端单轮内突增溢出。
/// </summary>
public sealed class PreRequestBudgetGuardTests {
    private sealed class RecordingSnipService : IHistorySnipService {
        public List<int> TokenLimits { get; } = [];

        public Task<SnipResult> SnipHistoryAsync(MessageList history, SnipOptions options, CancellationToken ct = default) =>
            Task.FromResult(new SnipResult());

        public Task<SnipResult> SnipByTokenLimitAsync(MessageList history, int maxTokens, CancellationToken ct = default) {
            TokenLimits.Add(maxTokens);
            return Task.FromResult(new SnipResult { MessagesRemoved = 1 });
        }

        public Task<SnipResult> SnipByMessageCountAsync(MessageList history, int maxMessages, CancellationToken ct = default) =>
            Task.FromResult(new SnipResult());
    }

    private static PreRequestBudgetGuardMiddleware CreateGuard(int ctxMax, out RecordingSnipService snip) {
        var contextManager = new Mock<IChatContextManager>();
        contextManager.Setup(c => c.GetContextMaxTokens()).Returns(ctxMax);
        snip = new RecordingSnipService();
        return new PreRequestBudgetGuardMiddleware(snip, contextManager.Object);
    }

    private static async Task RunHooksAsync(PreRequestBudgetGuardMiddleware guard, MessageList history) {
        var context = new QueryMiddlewareContext {
            UserInput = "检查预算",
            ChatHistory = history,
            Config = new QueryEngineConfig(),
            Kernel = new Mock<IChatClient>().Object,
            ToolRegistry = new Mock<IToolRegistry>().Object
        };
        await guard.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
        foreach (var hook in context.BeforeIterationHooks) {
            await hook(context, CancellationToken.None).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task BelowPreventiveThreshold_DoesNotSnip() {
        await using var guard = CreateGuard(1000, out var snip);
        var history = new MessageList();
        history.AddUserMessage(new string('a', 1000)); // ≈250 token

        await RunHooksAsync(guard, history).ConfigureAwait(true);

        snip.TokenLimits.Should().BeEmpty("低于 0.7 占比不裁剪");
    }

    [Fact]
    public async Task PreventiveRatio_SnipsTo65Percent() {
        await using var guard = CreateGuard(1000, out var snip);
        var history = new MessageList();
        history.AddUserMessage(new string('中', 1200)); // CJK 1.5 字/token ≈800 token → ratio 0.8

        await RunHooksAsync(guard, history).ConfigureAwait(true);

        snip.TokenLimits.Should().ContainSingle().Which.Should().Be(650, "预防性裁剪目标 65% 窗口");
    }

    [Fact]
    public async Task EmergencyRatio_SnipsTo50Percent() {
        await using var guard = CreateGuard(1000, out var snip);
        var history = new MessageList();
        history.AddUserMessage(new string('中', 2000)); // ≈1333 token → ratio 1.33

        await RunHooksAsync(guard, history).ConfigureAwait(true);

        snip.TokenLimits.Should().ContainSingle().Which.Should().Be(500, "紧急裁剪目标 50% 窗口");
    }

    [Fact]
    public async Task MissingContextManager_AddsNoHook() {
        var snip = new RecordingSnipService();
        await using var guard = new PreRequestBudgetGuardMiddleware(snip, contextManager: null);
        var context = new QueryMiddlewareContext {
            UserInput = "检查预算",
            ChatHistory = new MessageList(),
            Config = new QueryEngineConfig(),
            Kernel = new Mock<IChatClient>().Object,
            ToolRegistry = new Mock<IToolRegistry>().Object
        };

        await guard.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        context.BeforeIterationHooks.Should().BeEmpty("缺上下文窗口来源时不注册守卫");
    }
}
