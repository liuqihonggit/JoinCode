namespace NativeTui.Host.Tests;

/// <summary>
/// M8-5 命令面交互回调测试 — EngineSession.DispatchSlashCommandAsync 的回调透传链：
/// TuiCommandCallbacks（Bind 延迟绑定容器，对齐 TuiPermissionBridge 形态）→
/// SlashCommandRunner.RunAsync（clearScreen/onExitRequested 透传）→ 命令执行
/// （ClearCommand context.ClearScreen?.Invoke / ExitCommand ShouldContinue=false → onExitRequested）。
/// fake DI（IChatService/ICodeService/IPlanService/IToolRegistry stub）驱动真实命令注册链
/// （ChatCommandRegistry 兜底生成注册 — /clear、/exit 命中斜杠不经 MCP），零引擎依赖纯回调管道验证；
/// 组装层全链（Program 接线 → ReplShell 能力）由主智能体收编 ReplShell 公开入口后闭环。
/// </summary>
public sealed class EngineSessionCommandCallbacksTests {
    /// <summary>/clear 回调透传 — ClearCommand 执行链触发 clearScreen 回调 + 引擎历史真实清除。</summary>
    [Fact]
    public async Task Dispatch_Clear_InvokesClearScreenCallbackAndClearsHistory() {
        var chat = new FakeChatService();
        using var services = BuildFakeServices(chat);
        var cleared = false;
        var callbacks = new TuiCommandCallbacks();
        callbacks.Bind(clearScreen: () => cleared = true, onExitRequested: null);
        var consumer = new InProcessEventConsumer();

        await EngineSession.DispatchSlashCommandAsync(services, "/clear", consumer, callbacks, CancellationToken.None);

        Assert.True(cleared, "/clear 应触发 clearScreen 回调（清 TUI 滚动区接线点）");
        Assert.Equal(1, chat.ClearHistoryCalls);
    }

    /// <summary>/clear 输出上屏 — 清空反馈经 consumer.OnText（AnsiStripper 剥离后）→ token_delta。</summary>
    [Fact]
    public async Task Dispatch_Clear_EmitsFeedbackText() {
        var chat = new FakeChatService();
        using var services = BuildFakeServices(chat);
        var callbacks = new TuiCommandCallbacks();
        var consumer = new InProcessEventConsumer();

        await EngineSession.DispatchSlashCommandAsync(services, "/clear", consumer, callbacks, CancellationToken.None);

        var events = consumer.DrainEvents();
        Assert.Contains(events, static e =>
            e is BridgeEvent.TokenDelta { Text: { } text } && text.Contains("已清空", StringComparison.Ordinal));
    }

    /// <summary>/exit 回调透传 — ExitCommand 非交互直退（ShouldContinue=false）→ onExitRequested 回调触发。</summary>
    [Fact]
    public async Task Dispatch_Exit_InvokesExitRequestedCallback() {
        var chat = new FakeChatService();
        using var services = BuildFakeServices(chat);
        var exitRequested = false;
        var callbacks = new TuiCommandCallbacks();
        callbacks.Bind(clearScreen: null, onExitRequested: () => exitRequested = true);
        var consumer = new InProcessEventConsumer();

        await EngineSession.DispatchSlashCommandAsync(services, "/exit", consumer, callbacks, CancellationToken.None);

        Assert.True(exitRequested, "/exit ShouldContinue=false 应触发 onExitRequested 回调（TUI 主循环结束接线点）");
    }

    /// <summary>未绑定降级 — Bind 前回调为 null：/clear、/exit 命令本体照常执行不抛（?.Invoke 安全跳过）。</summary>
    [Fact]
    public async Task Dispatch_UnboundCallbacks_ClearAndExitExecuteSafely() {
        var chat = new FakeChatService();
        using var services = BuildFakeServices(chat);
        var callbacks = new TuiCommandCallbacks();

        await EngineSession.DispatchSlashCommandAsync(services, "/clear", new InProcessEventConsumer(), callbacks, CancellationToken.None);
        await EngineSession.DispatchSlashCommandAsync(services, "/exit", new InProcessEventConsumer(), callbacks, CancellationToken.None);

        Assert.Equal(1, chat.ClearHistoryCalls);
    }

    /// <summary>容器契约 — Bind 前回调 null + IsBound false。</summary>
    [Fact]
    public void TuiCommandCallbacks_BeforeBind_CallbacksAreNull() {
        var callbacks = new TuiCommandCallbacks();

        Assert.Null(callbacks.ClearScreen);
        Assert.Null(callbacks.OnExitRequested);
        Assert.False(callbacks.IsBound);
    }

    /// <summary>容器契约 — Bind 后委托同引用暴露 + IsBound true + 调用透传生效。</summary>
    [Fact]
    public void TuiCommandCallbacks_AfterBind_ExposesBoundDelegates() {
        var callbacks = new TuiCommandCallbacks();
        var cleared = false;
        var exited = false;
        Action clear = () => cleared = true;
        Action exit = () => exited = true;

        callbacks.Bind(clear, exit);

        Assert.Same(clear, callbacks.ClearScreen);
        Assert.Same(exit, callbacks.OnExitRequested);
        Assert.True(callbacks.IsBound);
        callbacks.ClearScreen!.Invoke();
        callbacks.OnExitRequested!.Invoke();
        Assert.True(cleared);
        Assert.True(exited);
    }

    /// <summary>fake DI — SlashCommandRunner 必需依赖（IChatService 真实记录 ClearHistory；
    /// ICodeService/IPlanService/IToolRegistry 为签名占位 stub — /clear、/exit 命中斜杠不触达）。</summary>
    private static ServiceProvider BuildFakeServices(FakeChatService chat) {
        return new ServiceCollection()
            .AddSingleton<IChatService>(chat)
            .AddSingleton<ICodeService>(new StubCodeService())
            .AddSingleton<IPlanService>(new StubPlanService())
            .AddSingleton<IToolRegistry>(new StubToolRegistry())
            .BuildServiceProvider();
    }

    /// <summary>IChatService 记录桩 — ClearHistoryAsync 计数（/clear 断言面），其余成员不支持。</summary>
    private sealed class FakeChatService : IChatService {
        public int ClearHistoryCalls;

        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) {
            ClearHistoryCalls++;
            return Task.CompletedTask;
        }

        public Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<string> SendMessageStreamAsync(string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LoadSessionMessagesAsync(IReadOnlyList<ApiMessageRecord> messages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CompactHistoryAsync(string summary, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => default;
    }

    /// <summary>ICodeService 签名占位桩 — BuildCommandServiceProvider 必需依赖，命令执行不触达。</summary>
    private sealed class StubCodeService : ICodeService {
        public Task<string> GenerateCodeAsync(string prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> AnalyzeCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> ExecuteCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>IPlanService 签名占位桩 — BuildCommandServiceProvider 必需依赖，命令执行不触达。</summary>
    private sealed class StubPlanService : IPlanService {
        public Task<string> ExecutePlanAsync(string userPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PlanExecutionResult> ExecutePlanWithResultAsync(string userPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>IToolRegistry 签名占位桩 — 使 CmdMap 构造（非 null 走斜杠优先路由）；/clear、/exit 命中斜杠，MCP 面不触达。</summary>
    private sealed class StubToolRegistry : IToolRegistry {
        public ValueTask DisposeAsync() => default;
        public Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RegisterToolsBatchAsync(IReadOnlyList<IToolHandler> handlers, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null, string? category = null) => throw new NotSupportedException();
        public Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default) => Task.FromResult<IToolHandler?>(null);
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default, ToolProgressCallback? onProgress = null) => throw new NotSupportedException();
        public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
