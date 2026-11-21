namespace NativeTui.Host.Tests;

/// <summary>
/// M7-3 权限确认 Host 链路测试 — TuiInteractionModule（bridge + handler 注册）+
/// TuiPermissionBridge（引擎线程 Confirm → shell 对话框挂起 → 用户按键解除，未绑定降级 Deny 防死锁）+
/// TuiPermissionConfirmationHandler（转发 bridge + 临时批准副作用 — Allow 1 分钟/AlwaysAllow 30 分钟，
/// 对齐 CLI CliPermissionConfirmationHandler 先例）。
/// </summary>
public sealed class TuiPermissionConfirmationTests {
    /// <summary>记录型假权限管理器 — 捕获临时批准调用。</summary>
    private sealed class FakePermissionManager : IToolPermissionManager {
        /// <summary>收到的 (工具名, 时长) 批准记录。</summary>
        public List<(string Tool, TimeSpan Duration)> Approvals { get; } = [];

        /// <summary>收到的等级批准记录。</summary>
        public List<CommandDangerLevel> LevelApprovals { get; } = [];

        /// <summary>收到的永久批准规则记录（TASK070 P1 — AlwaysAllow 持久化面）。</summary>
        public List<(string Tool, string? RuleContent)> PermanentApprovals { get; } = [];

        /// <summary>永久批准 — 记录不抛（AlwaysAllow 持久化失败仅记日志，fake 同语义）。</summary>
        public Task ApproveToolPermanentlyAsync(string toolName, string? ruleContent, CancellationToken cancellationToken = default) {
            PermanentApprovals.Add((toolName, ruleContent));
            return Task.CompletedTask;
        }

        /// <summary>未使用（引擎 PermissionRequest 在 JoinCode.Abstractions.Security，与 Core Repl 同名 — 全限定消歧）。</summary>
        public Task<JoinCode.Abstractions.Security.PermissionResult> CheckPermissionAsync(JoinCode.Abstractions.Security.PermissionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>未使用。</summary>
        public Task SetPermissionModeAsync(JoinCode.Abstractions.Security.PermissionMode mode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>未使用。</summary>
        public Task<JoinCode.Abstractions.Security.PermissionMode> GetCurrentModeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>未使用。</summary>
        public Task AddAllowedPromptAsync(string prompt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>未使用。</summary>
        public Task<int> StripDangerousRulesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>未使用。</summary>
        public Task RestoreDangerousRulesAsync(int ruleCount, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>记录工具批准。</summary>
        public void ApproveToolTemporarily(string toolName, TimeSpan duration) => Approvals.Add((toolName, duration));

        /// <summary>记录等级批准。</summary>
        public void ApproveLevelTemporarily(CommandDangerLevel level) => LevelApprovals.Add(level);

        /// <summary>未使用。</summary>
        public void RemoveTemporaryApproval(string toolName) {
        }

        /// <summary>未使用。</summary>
        public Task ClearCacheAsync() => Task.CompletedTask;
    }

    /// <summary>最小假引擎（ReplShell 构造依赖 — 对话框交互不触达引擎）。</summary>
    private sealed class IdleEngine : IReplEngine {
        /// <summary>未使用。</summary>
        public void Submit(string text) {
        }

        /// <summary>未使用。</summary>
        public void Interrupt() {
        }

        /// <summary>未使用。</summary>
        public void SetPermissionMode(string mode) {
        }

        /// <summary>空事件。</summary>
        public IReadOnlyList<BridgeEvent> DrainEvents() => [];

        /// <summary>存活。</summary>
        public bool IsAlive => true;
    }

    /// <summary>构造绑定 shell 的 bridge（seed 后待用）。</summary>
    private static (TuiPermissionBridge Bridge, ReplShell Shell) CreateBound() {
        var bridge = new TuiPermissionBridge();
        var shell = new ReplShell(new IdleEngine(), new BannerItem("0.0.0", "m", "API", "/tmp"), new StringWriter());
        shell.SeedContent();
        bridge.Bind(shell);
        return (bridge, shell);
    }

    /// <summary>轮询等待谓词成立（上限 2 秒 — Confirm 在后台线程 Begin 后即成立）。</summary>
    private static void WaitUntil(Func<bool> condition) {
        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline) {
            if (condition())
                return;
            Thread.Sleep(10);
        }
    }

    /// <summary>后台线程发起 Confirm 并以按键解除 — 返回引擎侧拿到的动作。</summary>
    private static async Task<PermissionConfirmAction> ConfirmWithKeyAsync(TuiPermissionBridge bridge, ReplShell shell, char key) {
        var confirmTask = Task.Run(() => bridge.Confirm("Bash", "run tests"));
        WaitUntil(() => shell.Permission.IsPending);
        shell.HandleKey(new ConsoleKeyInfo(key, ConsoleKey.D1 + (key - '1'), false, false, false));
        return (await confirmTask).Action;
    }

    // ---- TuiInteractionModule ----

    /// <summary>模块注册 — bridge 单例 + IPermissionConfirmationHandler → TuiPermissionConfirmationHandler。</summary>
    [Fact]
    public void Module_RegistersBridgeAndHandler() {
        var services = new ServiceCollection();
        var context = new AppModuleContext {
            Options = new CommandLineOptions(),
            Config = new WorkflowConfig(),
        };

        new TuiInteractionModule(new TuiPermissionBridge(), new TuiAskBridge()).ConfigureServices(services, context);

        Assert.Contains(services, d => d.ServiceType == typeof(TuiPermissionBridge));
        Assert.Contains(services, d => d.ServiceType == typeof(TuiAskBridge));
        Assert.Contains(services, d => d.ServiceType == typeof(IInteractiveService)
            && d.ImplementationType == typeof(TuiInteractiveService));
        Assert.Contains(services, d => d.ServiceType == typeof(IPermissionConfirmationHandler)
            && d.ImplementationType == typeof(TuiPermissionConfirmationHandler));
    }

    // ---- TuiPermissionBridge ----

    /// <summary>未绑定 shell — Confirm 直接 Deny（引擎极早期请求防死锁降级）。</summary>
    [Fact]
    public void Bridge_ConfirmWithoutShell_Denies() {
        var bridge = new TuiPermissionBridge();

        Assert.Equal(PermissionConfirmAction.Deny, bridge.Confirm("Bash", "run tests").Action);
    }

    /// <summary>绑定后 — Confirm 挂起对话框（工具名/描述透传）→ 数字 1 解除 → Allow。</summary>
    [Fact]
    public async Task Bridge_ConfirmRoutesThroughShellDialog_Allow() {
        var (bridge, shell) = CreateBound();

        var action = await ConfirmWithKeyAsync(bridge, shell, '1');

        Assert.Equal(PermissionConfirmAction.Allow, action);
        Assert.Equal("Bash", shell.Permission.Request?.ToolName ?? "Bash");
        Assert.False(shell.Permission.IsPending);
    }

    /// <summary>数字 3 / Esc — Deny。</summary>
    [Fact]
    public async Task Bridge_ConfirmRoutesThroughShellDialog_Deny() {
        var (bridge, shell) = CreateBound();
        Assert.Equal(PermissionConfirmAction.Deny, await ConfirmWithKeyAsync(bridge, shell, '3'));

        var (bridge2, shell2) = CreateBound();
        var denyTask = Task.Run(() => bridge2.Confirm("Bash", "x"));
        WaitUntil(() => shell2.Permission.IsPending);
        shell2.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        Assert.Equal(PermissionConfirmAction.Deny, (await denyTask).Action);
    }

    /// <summary>数字 2 — AlwaysAllow。</summary>
    [Fact]
    public async Task Bridge_ConfirmRoutesThroughShellDialog_AlwaysAllow() {
        var (bridge, shell) = CreateBound();

        Assert.Equal(PermissionConfirmAction.AlwaysAllow, await ConfirmWithKeyAsync(bridge, shell, '2'));
    }

    // ---- TuiPermissionConfirmationHandler ----

    /// <summary>Allow — 临时批准 1 分钟（对齐 CLI 先例）。</summary>
    [Fact]
    public void Handler_Allow_TemporarilyApprovesOneMinute() {
        var manager = new FakePermissionManager();
        var handler = new TuiPermissionConfirmationHandler(new FakeBridge(PermissionConfirmAction.Allow), manager);

        var result = handler.Confirm("Bash", "run tests");

        Assert.Equal(PermissionConfirmAction.Allow, result.Action);
        Assert.Equal([("Bash", TimeSpan.FromMinutes(1))], manager.Approvals);
    }

    /// <summary>AlwaysAllow — 持久化批准（DSG046 P1 — 对齐基准 settings.json 永久规则：
    /// ApproveToolPermanentlyAsync 而非临时 30 分钟；旧"TemporarilyApprovesThirtyMinutes"推翻）。</summary>
    [Fact]
    public void Handler_AlwaysAllow_PermanentlyApproves() {
        var manager = new FakePermissionManager();
        var handler = new TuiPermissionConfirmationHandler(new FakeBridge(PermissionConfirmAction.AlwaysAllow), manager);

        handler.Confirm("Bash", "run tests");

        Assert.Equal([("Bash", (string?)null)], manager.PermanentApprovals);
        // AlwaysAllow 不再走临时批准（Approvals 仅 Allow 档）
        Assert.Empty(manager.Approvals);
    }

    /// <summary>Deny — 不产生临时批准。</summary>
    [Fact]
    public void Handler_Deny_NoTemporaryApproval() {
        var manager = new FakePermissionManager();
        var handler = new TuiPermissionConfirmationHandler(new FakeBridge(PermissionConfirmAction.Deny), manager);

        Assert.Equal(PermissionConfirmAction.Deny, handler.Confirm("Bash", "run tests").Action);
        Assert.Empty(manager.Approvals);
    }

    /// <summary>prompt 携带 levelTag — 同步批准对应等级（CLI 先例）。</summary>
    [Fact]
    public void Handler_LevelTagInPrompt_ApprovesLevel() {
        var manager = new FakePermissionManager();
        var handler = new TuiPermissionConfirmationHandler(new FakeBridge(PermissionConfirmAction.Allow), manager);

        handler.Confirm("Bash", "[红灯ask] run build");

        Assert.Contains(manager.LevelApprovals, l => l == CommandDangerLevel.Execution);
    }

    /// <summary>固定动作假 bridge — handler 单元测试解耦 shell；捕获收到的触发原因规则内容（P5）。</summary>
    private sealed class FakeBridge(PermissionConfirmAction action, string? feedback = null) : TuiPermissionBridge {
        /// <summary>最后一次 Confirm 收到的 ruleContent（P5 透传断言用）。</summary>
        public string? LastRuleContent { get; private set; }

        /// <summary>返回固定动作与反馈，并记录规则内容。</summary>
        public override PermissionConfirmResult Confirm(string toolName, string confirmationPrompt, string? ruleContent = null) {
            LastRuleContent = ruleContent;
            return PermissionConfirmResult.Of(action, feedback);
        }
    }

    // ---- TASK069-P3 反馈透传 ----

    /// <summary>
    /// 拒绝并填写说明 — bridge 把用户原话带给引擎（Tab 展开输入 → 逐字输入 → Enter 提交）。
    /// 焦点移到「No」档后 Tab 进输入态，此时数字/字母属于正文而非快捷选档。
    /// </summary>
    [Fact]
    public async Task Bridge_CarriesRejectFeedback() {
        var (bridge, shell) = CreateBound();
        var confirmTask = Task.Run(() => bridge.Confirm("Bash", "run tests"));
        WaitUntil(() => shell.Permission.IsPending);

        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.DownArrow));
        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.DownArrow));
        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.Tab));
        Assert.True(shell.Permission.FeedbackInputActive);

        foreach (var ch in "用 ls")
        {
            shell.HandleRawKeyPress(KeyPress.Char(ch.ToString()));
        }

        // 输入态下数字不触发选档（仍进正文）
        shell.HandleRawKeyPress(KeyPress.Char("2"));
        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.Return));

        var result = await confirmTask;
        Assert.Equal(PermissionConfirmAction.Deny, result.Action);
        Assert.Equal("用 ls2", result.Feedback);
    }

    /// <summary>未填说明时 Feedback 为 null（拒绝文案保持原样 — 现状零变化）。</summary>
    [Fact]
    public async Task Bridge_WithoutFeedback_ReturnsNullFeedback() {
        var (bridge, shell) = CreateBound();
        var confirmTask = Task.Run(() => bridge.Confirm("Bash", "run tests"));
        WaitUntil(() => shell.Permission.IsPending);

        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.DownArrow));
        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.DownArrow));
        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.Return));

        var result = await confirmTask;
        Assert.Equal(PermissionConfirmAction.Deny, result.Action);
        Assert.Null(result.Feedback);
    }

    /// <summary>允许时填写的说明同样透传（基准 acceptFeedback 追加到工具结果）。</summary>
    [Fact]
    public async Task Bridge_CarriesAcceptFeedback() {
        var (bridge, shell) = CreateBound();
        var confirmTask = Task.Run(() => bridge.Confirm("Bash", "run tests"));
        WaitUntil(() => shell.Permission.IsPending);

        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.Tab));
        shell.HandleRawKeyPress(KeyPress.Char("r"));
        shell.HandleRawKeyPress(KeyPress.Char("u"));
        shell.HandleRawKeyPress(KeyPress.Named(KeyKind.Return));

        var result = await confirmTask;
        Assert.Equal(PermissionConfirmAction.Allow, result.Action);
        Assert.Equal("ru", result.Feedback);
    }

    /// <summary>handler 透传反馈（bridge 给出即原样返回 — handler 只加临时批准副作用）。</summary>
    [Fact]
    public void Handler_PassesFeedbackThrough() {
        var handler = new TuiPermissionConfirmationHandler(new FakeBridge(PermissionConfirmAction.Deny, "别删，改成备份"));

        var result = handler.Confirm("Bash", "rm -rf build");

        Assert.Equal(PermissionConfirmAction.Deny, result.Action);
        Assert.Equal("别删，改成备份", result.Feedback);
    }

    // ---- TASK069-P5 触发原因透传 ----

    /// <summary>bridge 把引擎给的规则内容带进挂起请求（对话框据此渲染原因是唯一数据源）。</summary>
    [Fact]
    public async Task Bridge_CarriesRuleContentIntoRequest() {
        var (bridge, shell) = CreateBound();
        var confirmTask = Task.Run(() => bridge.Confirm("WebFetch", "是否允许访问？", "domain:example.com"));
        WaitUntil(() => shell.Permission.IsPending);

        Assert.Equal("domain:example.com", shell.Permission.Request?.RuleContent);

        shell.HandleRawKeyPress(KeyPress.Char("3"));
        await confirmTask;
    }

    /// <summary>无规则内容时挂起请求保持空串（对话框整区折叠 — 不编造原因）。</summary>
    [Fact]
    public async Task Bridge_WithoutRuleContent_KeepsEmpty() {
        var (bridge, shell) = CreateBound();
        var confirmTask = Task.Run(() => bridge.Confirm("Bash", "run tests"));
        WaitUntil(() => shell.Permission.IsPending);

        Assert.Equal(string.Empty, shell.Permission.Request?.RuleContent);

        shell.HandleRawKeyPress(KeyPress.Char("3"));
        await confirmTask;
    }

    /// <summary>handler 把引擎的 ruleContent 原样转给 bridge（中间层不得丢弃或无中生有）。</summary>
    [Fact]
    public void Handler_PassesRuleContentToBridge() {
        var bridge = new FakeBridge(PermissionConfirmAction.Allow);
        var handler = new TuiPermissionConfirmationHandler(bridge, new FakePermissionManager());

        handler.Confirm("WebFetch", "是否允许访问？", "domain:example.com");

        Assert.Equal("domain:example.com", bridge.LastRuleContent);
    }

    /// <summary>引擎 DI 解析 handler（真 bridge 注入路径 — Program 组装等价冒烟）。</summary>
    [Fact]
    public async Task Module_HandlerResolvableWithRealBridge() {
        var services = new ServiceCollection();
        var context = new AppModuleContext {
            Options = new CommandLineOptions(),
            Config = new WorkflowConfig(),
        };
        var bridge = new TuiPermissionBridge();
        new TuiInteractionModule(bridge, new TuiAskBridge()).ConfigureServices(services, context);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<IPermissionConfirmationHandler>();

        Assert.IsType<TuiPermissionConfirmationHandler>(resolved);
        Assert.Equal(PermissionConfirmAction.Deny, (await resolved.ConfirmAsync("Bash", "x")).Action);
    }
}
