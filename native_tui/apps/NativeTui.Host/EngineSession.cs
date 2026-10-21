namespace NativeTui.Host;

/// <summary>
/// 引擎会话组装 — BridgeServeCommand 的进程内直连版（ADR 0143 阶段二组装胶水）：
/// EngineSessionFactory.CreateGuiSessionAsync 建引擎会话 → 解析 PermissionChecker（等模式/安全闸初始化）与
/// IFastModeService → EngineTurnClient 构造（回合执行器延迟绑定 SessionController — 首次 submit 时绑定，
/// consumer 即事件出口；权限应用器绑定 PermissionChecker 安全闸）→ 种子 session_info 握手事件（banner 真实数据源）。
/// 引擎 Host 生命周期由本类持有（IAsyncDisposable — await using 保证异步 Dispose 避免线程池死锁）。
/// </summary>
public sealed class EngineSession : IAsyncDisposable {
    private readonly EngineSessionFactory.Result _engine;
    private readonly EngineTurnClient _client;
    private readonly TuiPermissionBridge _permissionBridge;
    private readonly PermissionChecker? _checker;
    private readonly IFastModeService? _fastMode;
    private readonly TuiCommandCallbacks _commandCallbacks;
    private readonly BackgroundTaskEventPump? _backgroundPump;

    private EngineSession(EngineSessionFactory.Result engine, EngineTurnClient client, TuiPermissionBridge permissionBridge, PermissionChecker? checker, IFastModeService? fastMode, TuiCommandCallbacks commandCallbacks, BackgroundTaskEventPump? backgroundPump) {
        _engine = engine;
        _client = client;
        _permissionBridge = permissionBridge;
        _checker = checker;
        _fastMode = fastMode;
        _commandCallbacks = commandCallbacks;
        _backgroundPump = backgroundPump;
    }

    /// <summary>回合客户端 — 主循环消费面（Submit/Interrupt/SetPermissionMode/DrainEvents）。</summary>
    public EngineTurnClient Client => _client;

    /// <summary>权限确认桥 — Program 在 shell 构造后 Bind（引擎 PendingConfirmation → TUI 对话框闭环，M7-3）。</summary>
    public TuiPermissionBridge PermissionBridge => _permissionBridge;

    /// <summary>命令交互回调桥 — Program 在 shell 构造后 Bind（/clear 清滚动区、/exit 结束主循环 → ReplShell 能力，M8-5）。</summary>
    public TuiCommandCallbacks CommandCallbacks => _commandCallbacks;

    /// <summary>引擎服务容器 — 宿主组装层消费（命令清单/历史种子等只读解析）。</summary>
    public IServiceProvider Services => _engine.Services;

    /// <summary>当前会话标识 — 历史种子排除当前会话防双份。</summary>
    public string SessionId => _engine.SessionId;

    /// <summary>重发 session_info 事件 — 模型/供应商切换等运行时状态变化后刷新 banner（ReplBridgeDriver.ApplySessionInfo 差分上屏）。</summary>
    public void RefreshSessionInfo() => _client.Enqueue(BuildSessionInfoEvent(_checker, _fastMode, _engine.Config));

    /// <summary>TUI 静默日志 provider（internal — 测试断言非 Console：Console 直写 stderr 与 inline 渲染交错致光标错乱/横向线残影）。
    /// 错误反馈走 EngineTurnClient Error 事件链（● 回合执行失败），不依赖引擎日志。</summary>
    internal static ILoggerProvider LoggerProvider => SilentLoggerProvider.Instance;

    /// <summary>组装引擎会话 — 建会话（TuiInteractionModule 注册权限确认 handler；静默 logger —
    /// Console 日志直写 stderr 会与 inline 渲染交错破坏光标追踪，错误反馈走 EngineTurnClient Error 事件链）
    /// → 解析权限/模型 → 构造回合客户端 → 种子 session_info；失败时释放已建引擎 Host。</summary>
    public static async Task<EngineSession> CreateAsync(CancellationToken cancellationToken = default) {
        var permissionBridge = new TuiPermissionBridge();
        var engine = await EngineSessionFactory.CreateGuiSessionAsync(
            extraModules: [new TuiInteractionModule(permissionBridge)],
            loggerProvider: LoggerProvider,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        try {
            var checker = await ResolvePermissionCheckerAsync(engine.Services).ConfigureAwait(false);
            var fastMode = engine.Services.GetService<IFastModeService>();
            var mainAgent = EngineAgentFactory.CreateMainAgent(engine.Services, new Session().ObjectId);

            // M8-5 命令交互回调桥 — 回调管道随回合执行器闭包传递（Bind 前回调为 null 安全降级，
            // Program 在 shell 构造后 Bind：/clear → 清滚动区、/exit → 主循环结束）
            var commandCallbacks = new TuiCommandCallbacks();

            // M8-8 后台任务事件泵 — 订阅 ForkCompleted/TeammateCompleted 直推 consumer
            // （绕开回合作用域 SubAgentEventChannel 的 idle 丢弃 gap）；两面任一可解析即建泵
            var forkManager = engine.Services.GetService<IForkSubAgentManager>();
            var teammateExecutor = engine.Services.GetService<IInProcessTeammateTaskExecutor>();
            BackgroundTaskEventPump? backgroundPump = null;
            if (forkManager is not null || teammateExecutor is not null) {
                backgroundPump = new BackgroundTaskEventPump(forkManager, teammateExecutor);
            }

            // 首次 submit 时绑定回合驱动器 — consumer 即进程内事件出口（client 构造，全程同一实例，对齐 BridgeServeCommand 先例）
            SessionController? controller = null;
            var client = new EngineTurnClient(
                (input, consumer, turnCt) => {
                    // TASK064 斜杠命令分流（对齐 CliSession.ProcessInputAsync：/ 开头走命令执行不进 LLM）
                    if (input.StartsWith('/')) {
                        return DispatchSlashCommandAsync(engine.Services, input, consumer, commandCallbacks, turnCt);
                    }

                    controller ??= new SessionController(
                        engine.ChatService, consumer, new TurnDiffService(), engine.SessionId, engine.Services,
                        mainAgent: mainAgent);
                    return RunTurnAsync(controller, input, turnCt);
                },
                BuildPermissionModeApplier(checker),
                backgroundPump);

            if (backgroundPump is not null) {
                backgroundPump.Bind(client.Consumer);
                await backgroundPump.StartAsync().ConfigureAwait(false);
            }

            var session = new EngineSession(engine, client, permissionBridge, checker, fastMode, commandCallbacks, backgroundPump);
            client.Enqueue(BuildSessionInfoEvent(checker, fastMode, engine.Config));
            return session;
        } catch {
            await engine.Host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>解析引擎权限检查器（等模式/安全闸初始化完成；未注册返回 null — 降级为仅展示默认值）。</summary>
    private static async Task<PermissionChecker?> ResolvePermissionCheckerAsync(IServiceProvider services) {
        if (services.GetService<SecurityPermissionChecker>() is not PermissionChecker checker)
            return null;

        await checker.ModeInitialization.ConfigureAwait(false);
        return checker;
    }

    /// <summary>组装会话握手事件（internal — 测试断言 SetPrimaryModel 后 Model 字段更新）。版本（引擎程序集）/当前模型（FastMode 主模型，未设置为 null）/
    /// 权限模式（安全闸后的实际值）/bypass 可用性（!DisableBypassGate）/cwd/供应商（WorkflowConfig.Provider.Vendor，TASK058 banner 显示）/
    /// 登录态（Provider.ApiKey 非空 = 能对话，TASK060 TUI 提交门禁依据）。</summary>
    internal static BridgeEvent.SessionInfo BuildSessionInfoEvent(PermissionChecker? checker, IFastModeService? fastMode, WorkflowConfig? config = null) {
        var version = typeof(EngineSessionFactory).Assembly.GetName().Version is { } v ? v.ToString(3) : "0.0.0";
        var mode = checker?.CurrentMode ?? EnginePermissionMode.Auto;
        var vendor = config?.Provider?.Vendor;
        var loggedIn = !string.IsNullOrWhiteSpace(config?.Provider?.ApiKey);
        return new BridgeEvent.SessionInfo(
            Version: version,
            Model: string.IsNullOrWhiteSpace(fastMode?.PrimaryModelId) ? null : fastMode.PrimaryModelId,
            PermissionMode: EnginePermissionModeExtensions.ToValue(mode),
            BypassAvailable: checker?.DisableBypassGate is not true,
            Cwd: Environment.CurrentDirectory,
            Vendor: string.IsNullOrWhiteSpace(vendor) ? null : vendor,
            LoggedIn: loggedIn);
    }

    /// <summary>构造权限模式应用器 — 解析字符串（FromValue）→ 应用（CurrentMode setter 含 bypass 安全闸）→
    /// 读回实际生效值转字符串（被闸拦截时与请求值不同）；未知值/无权限系统返回 null（client 发 Error 诱导）。</summary>
    private static EngineTurnClient.PermissionModeApplier? BuildPermissionModeApplier(PermissionChecker? checker) {
        if (checker is null)
            return null;

        return mode => {
            if (EnginePermissionModeExtensions.FromValue(mode) is not { } parsed)
                return null;

            checker.CurrentMode = parsed;
            return EnginePermissionModeExtensions.ToValue(checker.CurrentMode);
        };
    }

    /// <summary>执行单个回合 — SessionTurnResult 翻译为 EngineTurnClient 异常语义。
    /// 权限确认已改 ConfirmAsync 异步等待（ADR 0142）：Host 无 UI 时处理器自动 Deny，
    /// 工具拒绝走回合失败 → Error 事件（消息含 Esc 打断诱导），无抛异常重试路径。</summary>
    private static async Task RunTurnAsync(SessionController controller, string input, CancellationToken ct) {
        var result = await controller.StreamResponseAsync(input, ct).ConfigureAwait(false);
        var ex = TranslateTurnResult(result);
        if (ex is not null)
            throw ex;
    }

    /// <summary>斜杠命令回合（TASK064 — 引擎 SlashCommandRunner 共享链路：解析→路由→执行→输出捕获）：
    /// 输出经 AnsiStripper 剥离 SGR 色码（命令侧彩色输出与 TUI Cell/StyleId 渲染模型不兼容 —
    /// ESC 零宽跳过后残骸显示为 "38;2;153;153;153m──" 形态）再经 consumer.OnText/OnDone 上屏
    ///（AssistantMessage Markdown 渲染）；未知命令同样上屏诱导（/help 提示）— 不落 LLM
    ///（对齐"命令不触发 AI"：注册命令走命令执行，未知命令报错，两者都不消耗模型调用）。
    /// M8-5 交互回调接线：clearScreen/onExitRequested 经 TuiCommandCallbacks 透传（/clear 清 TUI 滚动区、
    /// /exit 结束主循环；Bind 前为 null — 命令侧 ?.Invoke 安全降级对齐 Runner 可空语义）；
    /// confirm/prompt/readPassword 首版不接（TUI raw mode 无同步 Console 通道 — 模态对话框增强项遗留，
    /// 命令侧降级：确认默认拒绝/输入 null）。internal static — 回调透传链测试面（fake DI 驱动真实命令注册链）。
    /// TASK065 F1 融合 — onExitRequested 包装带命令名（Runner 回调无参 → 解析 input 补 reason 上提示行）。</summary>
    internal static async Task DispatchSlashCommandAsync(
        IServiceProvider services, string input, IEventConsumer consumer, TuiCommandCallbacks commandCallbacks, CancellationToken ct) {
        var result = await SlashCommandRunner.RunAsync(
            input, services,
            clearScreen: commandCallbacks.ClearScreen,
            confirm: null,
            prompt: null,
            readPassword: null,
            onExitRequested: commandCallbacks.OnExitRequested,
            cancellationToken: ct).ConfigureAwait(false);

        var output = AnsiStripper.Strip(result.Output).TrimEnd();
        if (output.Length > 0) {
            consumer.OnText(output);
        }

        consumer.OnDone(usage: null, modelId: null);
    }

    /// <summary>翻译轮次结果 — 成功=null（client 发 turn_completed）；取消=OperationCanceledException（turn_interrupted）；
    /// 超时/错误=InvalidOperationException（client 发 Error 事件，消息含 Host 交互诱导 — Esc 打断 + 网络检查）。</summary>
    internal static Exception? TranslateTurnResult(SessionTurnResult result) {
        if (result.Succeeded)
            return null;
        if (result.WasCancelled)
            return new OperationCanceledException();
        if (result.TimedOut)
            return new InvalidOperationException(
                $"API 请求超时（{result.TimeoutMs}ms 无响应）。可按 Esc 打断后重试，或检查网络连接与 API 服务状态。");
        return new InvalidOperationException(result.ErrorMessage ?? "回合执行失败（未知错误）");
    }

    /// <summary>释放 — 后台任务泵退订（先于引擎 Host — Actor 释放后事件源消失）+ 取消在途回合（client.Dispose 快速返回）+ 异步释放引擎 Host。</summary>
    public async ValueTask DisposeAsync() {
        if (_backgroundPump is not null) {
            await _backgroundPump.DisposeAsync().ConfigureAwait(false);
        }

        _client.Dispose();
        await _engine.Host.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// TUI 命令交互回调桥（M8-5）— SlashCommandRunner 交互回调的延迟绑定容器：
/// EngineSession.CreateAsync 组装期创建并随回合执行器闭包传递（Bind 前回调为 null —
/// 命令侧 ?.Invoke 安全降级，对齐 Runner 可空语义：清屏跳过/退出信号丢弃/确认默认拒绝）；
/// Program 在 shell 构造后 Bind：/clear → ReplShell 清滚动区、/exit → ReplShell 主循环结束。
/// 对齐 TuiPermissionBridge.Bind 延迟绑定形态（shell 构造晚于引擎会话，引擎回合线程并发读 — volatile）。
/// </summary>
public sealed class TuiCommandCallbacks {
    private volatile Action? _clearScreen;
    private volatile Action? _onExitRequested;

    /// <summary>绑定 TUI 能力（Program 交互模式组装点 — shell 构造后、Run 前调用；重绑幂等覆盖）。</summary>
    public void Bind(Action? clearScreen, Action? onExitRequested) {
        _clearScreen = clearScreen;
        _onExitRequested = onExitRequested;
    }

    /// <summary>清屏回调（/clear → 清 TUI 滚动区，保留 banner+spinner 常驻项；Bind 前为 null — 命令侧跳过）。</summary>
    public Action? ClearScreen => _clearScreen;

    /// <summary>退出请求回调（/exit ShouldContinue=false → 结束 TUI 主循环；Bind 前为 null — 信号降级丢弃）。</summary>
    public Action? OnExitRequested => _onExitRequested;

    /// <summary>已绑定任一回调（internal — Program 接线测试断言面）。</summary>
    internal bool IsBound => _clearScreen is not null || _onExitRequested is not null;
}
