namespace NativeTui.Host.Tests;

/// <summary>
/// S8 命令交互回调闭环测试 — TuiCommandCallbacks 交互面（BindInteractions 独立于 Bind 的
/// shell 能力面，互不覆盖）+ EngineSession.MapConfirmResult（权限对话框两档映射）+
/// DispatchSlashCommandAsync 交互回调透传链（fake DI 注册交互探针命令驱动真实 SlashCommandRunner 链路 —
/// context.Confirm/Prompt/ReadPassword 到容器回调的闭环；未绑定 null 时命令侧默认拒绝/空输入降级实证）。
/// confirm 引擎会话组装期闭环（TuiPermissionBridge TCS 桥）由 Program 接线后全链验收（S7 输入模态接线未决）。
/// </summary>
public sealed class EngineSessionInteractionCallbacksTests
{
    /// <summary>交互探针命令名（confirm 面）。</summary>
    private const string ConfirmCommandName = "probeconfirm";

    /// <summary>交互探针命令名（prompt 面）。</summary>
    private const string PromptCommandName = "probeprompt";

    /// <summary>交互探针命令名（readPassword 面）。</summary>
    private const string PasswordCommandName = "probepassword";

    // ---- 容器契约 ----

    /// <summary>容器契约 — BindInteractions 前三回调均 null。</summary>
    [Fact]
    public void TuiCommandCallbacks_BeforeBindInteractions_CallbacksAreNull()
    {
        var callbacks = new TuiCommandCallbacks();

        Assert.Null(callbacks.Confirm);
        Assert.Null(callbacks.Prompt);
        Assert.Null(callbacks.ReadPassword);
        Assert.False(callbacks.IsBound);
    }

    /// <summary>容器契约 — BindInteractions 后委托同引用暴露 + 调用透传生效。</summary>
    [Fact]
    public void TuiCommandCallbacks_AfterBindInteractions_ExposesBoundDelegates()
    {
        var callbacks = new TuiCommandCallbacks();
        Func<string, bool> confirm = _ => true;
        Func<string, string?> prompt = _ => "input";
        Func<string, string?> readPassword = _ => "secret";

        callbacks.BindInteractions(confirm, prompt, readPassword);

        Assert.Same(confirm, callbacks.Confirm);
        Assert.Same(prompt, callbacks.Prompt);
        Assert.Same(readPassword, callbacks.ReadPassword);
        Assert.True(callbacks.IsBound);
        Assert.True(callbacks.Confirm!("x"));
        Assert.Equal("input", callbacks.Prompt!("x"));
        Assert.Equal("secret", callbacks.ReadPassword!("x"));
    }

    /// <summary>面隔离 — Bind（shell 能力面）不触碰交互面（Program 接线不抹组装期 confirm 闭环）。</summary>
    [Fact]
    public void TuiCommandCallbacks_BindShellSurface_DoesNotTouchInteractions()
    {
        var callbacks = new TuiCommandCallbacks();
        Func<string, bool> confirm = _ => true;
        callbacks.BindInteractions(confirm, null, null);

        callbacks.Bind(clearScreen: () => { }, onExitRequested: null);

        Assert.Same(confirm, callbacks.Confirm);
        Assert.Null(callbacks.Prompt);
        Assert.Null(callbacks.ReadPassword);
    }

    /// <summary>面隔离 — BindInteractions 不触碰 shell 能力面。</summary>
    [Fact]
    public void TuiCommandCallbacks_BindInteractions_DoesNotTouchShellSurface()
    {
        var callbacks = new TuiCommandCallbacks();
        Action clear = () => { };
        callbacks.Bind(clear, null);

        callbacks.BindInteractions(null, null, null);

        Assert.Same(clear, callbacks.ClearScreen);
        Assert.Null(callbacks.Confirm);
    }

    // ---- confirm 两档映射 ----

    /// <summary>确认映射 — Allow/AlwaysAllow → true（确认），Deny → false（拒绝）。</summary>
    [Theory]
    [InlineData(PermissionConfirmAction.Allow, true)]
    [InlineData(PermissionConfirmAction.AlwaysAllow, true)]
    [InlineData(PermissionConfirmAction.Deny, false)]
    public void MapConfirmResult_AllowMapsTrueDenyMapsFalse(PermissionConfirmAction action, bool expected)
    {
        Assert.Equal(expected, EngineSession.MapConfirmResult(action));
    }

    // ---- Dispatch 透传链（真实 SlashCommandRunner 链路 + fake 交互探针命令） ----

    /// <summary>confirm 透传 — 命令确认消息到达容器回调 + 返回值回传命令。</summary>
    [Fact]
    public async Task Dispatch_ConfirmCommand_InvokesConfirmCallback()
    {
        string? received = null;
        var callbacks = new TuiCommandCallbacks();
        callbacks.BindInteractions(
            confirm: message => { received = message; return true; },
            prompt: null,
            readPassword: null);
        var probe = new InteractionProbeCommand(ConfirmCommandName);
        using var services = BuildFakeServices(probe);

        await EngineSession.DispatchSlashCommandAsync(services, $"/{ConfirmCommandName}", new InProcessEventConsumer(), callbacks, CancellationToken.None);

        Assert.Equal("确认继续吗？", received);
        Assert.True(probe.ConfirmOutcome == true, "命令侧应收到回调返回的 true（确认）");
    }

    /// <summary>confirm 拒绝透传 — 回调返回 false 回传命令（确认取消路径）。</summary>
    [Fact]
    public async Task Dispatch_ConfirmCommand_DenialPropagatesToCommand()
    {
        var callbacks = new TuiCommandCallbacks();
        callbacks.BindInteractions(confirm: _ => false, prompt: null, readPassword: null);
        var probe = new InteractionProbeCommand(ConfirmCommandName);
        using var services = BuildFakeServices(probe);

        await EngineSession.DispatchSlashCommandAsync(services, $"/{ConfirmCommandName}", new InProcessEventConsumer(), callbacks, CancellationToken.None);

        Assert.Equal(false, probe.ConfirmOutcome);
    }

    /// <summary>prompt 透传 — 输入消息到达容器回调 + 输入值回传命令。</summary>
    [Fact]
    public async Task Dispatch_PromptCommand_InvokesPromptCallback()
    {
        string? received = null;
        var callbacks = new TuiCommandCallbacks();
        callbacks.BindInteractions(
            confirm: null,
            prompt: message => { received = message; return "用户输入的文本"; },
            readPassword: null);
        var probe = new InteractionProbeCommand(PromptCommandName);
        using var services = BuildFakeServices(probe);

        await EngineSession.DispatchSlashCommandAsync(services, $"/{PromptCommandName}", new InProcessEventConsumer(), callbacks, CancellationToken.None);

        Assert.Equal("请输入内容：", received);
        Assert.Equal("用户输入的文本", probe.PromptOutcome);
    }

    /// <summary>readPassword 透传 — 密码消息到达容器回调 + 密码值回传命令。</summary>
    [Fact]
    public async Task Dispatch_ReadPasswordCommand_InvokesReadPasswordCallback()
    {
        string? received = null;
        var callbacks = new TuiCommandCallbacks();
        callbacks.BindInteractions(
            confirm: null,
            prompt: null,
            readPassword: message => { received = message; return "secret-token"; });
        var probe = new InteractionProbeCommand(PasswordCommandName);
        using var services = BuildFakeServices(probe);

        await EngineSession.DispatchSlashCommandAsync(services, $"/{PasswordCommandName}", new InProcessEventConsumer(), callbacks, CancellationToken.None);

        Assert.Equal("请输入密码：", received);
        Assert.Equal("secret-token", probe.PasswordOutcome);
    }

    /// <summary>未绑定降级 — 交互回调 null 时命令侧拿到 null（Runner 可空语义安全降级实证）。</summary>
    [Fact]
    public async Task Dispatch_UnboundInteractions_CommandGetsNullCallbacks()
    {
        var callbacks = new TuiCommandCallbacks();
        var probe = new InteractionProbeCommand(ConfirmCommandName);
        using var services = BuildFakeServices(probe);

        await EngineSession.DispatchSlashCommandAsync(services, $"/{ConfirmCommandName}", new InProcessEventConsumer(), callbacks, CancellationToken.None);

        Assert.True(probe.ConfirmOutcome is null, "未绑定 confirm 时命令侧 ?.Invoke 降级为 null（不抛不挂起）");
    }

    /// <summary>交互探针命令 — 消费 ChatCommandContext 的 Confirm/Prompt/ReadPassword 并记录回传结果。</summary>
    private sealed class InteractionProbeCommand(string name) : JoinCode.ChatCommands.IChatCommand
    {
        /// <summary>命令名。</summary>
        public string Name => name;

        /// <summary>命令描述。</summary>
        public string Description => "交互回调探针（测试专用）";

        /// <summary>用法说明。</summary>
        public string Usage => $"/{name}";

        /// <summary>命令别名。</summary>
        public string[] Aliases => [];

        /// <summary>参数提示。</summary>
        public string ArgumentHint => string.Empty;

        /// <summary>隐藏命令（测试探针不出现在命令清单）。</summary>
        public bool IsHidden => true;

        /// <summary>confirm 回传结果（未绑定为 null）。</summary>
        public bool? ConfirmOutcome { get; private set; }

        /// <summary>prompt 回传结果（未绑定为 null）。</summary>
        public string? PromptOutcome { get; private set; }

        /// <summary>readPassword 回传结果（未绑定为 null）。</summary>
        public string? PasswordOutcome { get; private set; }

        /// <summary>执行 — 三个交互面逐个调用并记录回传值。</summary>
        public Task<JoinCode.ChatCommands.ChatCommandResult> ExecuteAsync(JoinCode.ChatCommands.ChatCommandContext context)
        {
            ConfirmOutcome = ConfirmMessage is { } message ? context.Confirm?.Invoke(message) : null;
            PromptOutcome = PromptMessage is { } promptMessage ? context.Prompt?.Invoke(promptMessage) : null;
            PasswordOutcome = PasswordMessage is { } passwordMessage ? context.ReadPassword?.Invoke(passwordMessage) : null;
            System.Console.WriteLine($"探针 {name} 完成");
            return Task.FromResult(JoinCode.ChatCommands.ChatCommandResult.Continue());
        }

        /// <summary>confirm 消息（仅 confirm 探针命令携带）。</summary>
        private string? ConfirmMessage => name == ConfirmCommandName ? "确认继续吗？" : null;

        /// <summary>prompt 消息（仅 prompt 探针命令携带）。</summary>
        private string? PromptMessage => name == PromptCommandName ? "请输入内容：" : null;

        /// <summary>readPassword 消息（仅 password 探针命令携带）。</summary>
        private string? PasswordMessage => name == PasswordCommandName ? "请输入密码：" : null;
    }

    /// <summary>fake DI — 注册探针命令到真实 ChatCommandRegistry（SlashCommandRunner 命中斜杠不经 MCP）。</summary>
    private static ServiceProvider BuildFakeServices(InteractionProbeCommand probe)
    {
        var registry = new JoinCode.ChatCommands.ChatCommandRegistry();
        registry.Register(probe);
        return new ServiceCollection()
            .AddSingleton<IChatService>(new FakeChatService())
            .AddSingleton<ICodeService>(new StubCodeService())
            .AddSingleton<IPlanService>(new StubPlanService())
            .AddSingleton<IToolRegistry>(new StubToolRegistry())
            .AddSingleton(registry)
            .BuildServiceProvider();
    }

    /// <summary>IChatService 记录桩 — Runner 必需依赖占位（探针命令不触达）。</summary>
    private sealed class FakeChatService : IChatService
    {
        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    /// <summary>ICodeService 签名占位桩 — Runner 必需依赖，探针命令不触达。</summary>
    private sealed class StubCodeService : ICodeService
    {
        public Task<string> GenerateCodeAsync(string prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> AnalyzeCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> ExecuteCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>IPlanService 签名占位桩 — Runner 必需依赖，探针命令不触达。</summary>
    private sealed class StubPlanService : IPlanService
    {
        public Task<string> ExecutePlanAsync(string userPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PlanExecutionResult> ExecutePlanWithResultAsync(string userPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>IToolRegistry 签名占位桩 — 使 CmdMap 构造（非 null 走斜杠优先路由）；探针命令命中斜杠。</summary>
    private sealed class StubToolRegistry : IToolRegistry
    {
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
