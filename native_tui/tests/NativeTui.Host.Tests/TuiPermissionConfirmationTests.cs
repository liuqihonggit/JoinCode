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
        return await confirmTask;
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

        new TuiInteractionModule(new TuiPermissionBridge()).ConfigureServices(services, context);

        Assert.Contains(services, d => d.ServiceType == typeof(TuiPermissionBridge));
        Assert.Contains(services, d => d.ServiceType == typeof(IPermissionConfirmationHandler)
            && d.ImplementationType == typeof(TuiPermissionConfirmationHandler));
    }

    // ---- TuiPermissionBridge ----

    /// <summary>未绑定 shell — Confirm 直接 Deny（引擎极早期请求防死锁降级）。</summary>
    [Fact]
    public void Bridge_ConfirmWithoutShell_Denies() {
        var bridge = new TuiPermissionBridge();

        Assert.Equal(PermissionConfirmAction.Deny, bridge.Confirm("Bash", "run tests"));
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
        Assert.Equal(PermissionConfirmAction.Deny, await denyTask);
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

        var action = handler.Confirm("Bash", "run tests");

        Assert.Equal(PermissionConfirmAction.Allow, action);
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

        Assert.Equal(PermissionConfirmAction.Deny, handler.Confirm("Bash", "run tests"));
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

    /// <summary>固定动作假 bridge — handler 单元测试解耦 shell。</summary>
    private sealed class FakeBridge(PermissionConfirmAction action) : TuiPermissionBridge {
        /// <summary>返回固定动作。</summary>
        public override PermissionConfirmAction Confirm(string toolName, string confirmationPrompt) => action;
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
        new TuiInteractionModule(bridge).ConfigureServices(services, context);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<IPermissionConfirmationHandler>();

        Assert.IsType<TuiPermissionConfirmationHandler>(resolved);
        Assert.Equal(PermissionConfirmAction.Deny, await resolved.ConfirmAsync("Bash", "x"));
    }
}
