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

    private EngineSession(EngineSessionFactory.Result engine, EngineTurnClient client) {
        _engine = engine;
        _client = client;
    }

    /// <summary>回合客户端 — 主循环消费面（Submit/Interrupt/SetPermissionMode/DrainEvents）。</summary>
    public EngineTurnClient Client => _client;

    /// <summary>组装引擎会话 — 建会话 → 解析权限/模型 → 构造回合客户端 → 种子 session_info；失败时释放已建引擎 Host。</summary>
    public static async Task<EngineSession> CreateAsync(CancellationToken cancellationToken = default) {
        var engine = await EngineSessionFactory.CreateGuiSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        try {
            var checker = await ResolvePermissionCheckerAsync(engine.Services).ConfigureAwait(false);
            var fastMode = engine.Services.GetService<IFastModeService>();
            var mainAgent = EngineAgentFactory.CreateMainAgent(engine.Services, new Session().ObjectId);

            // 首次 submit 时绑定回合驱动器 — consumer 即进程内事件出口（client 构造，全程同一实例，对齐 BridgeServeCommand 先例）
            SessionController? controller = null;
            var client = new EngineTurnClient(
                (input, consumer, turnCt) => {
                    controller ??= new SessionController(
                        engine.ChatService, consumer, new TurnDiffService(), engine.SessionId, engine.Services,
                        mainAgent: mainAgent);
                    return RunTurnAsync(controller, input, turnCt);
                },
                BuildPermissionModeApplier(checker));

            var session = new EngineSession(engine, client);
            client.Enqueue(BuildSessionInfoEvent(checker, fastMode));
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

    /// <summary>组装会话握手事件 — 版本（引擎程序集）/当前模型（FastMode 主模型，未设置为 null）/
    /// 权限模式（安全闸后的实际值）/bypass 可用性（!DisableBypassGate）/cwd。</summary>
    private static BridgeEvent.SessionInfo BuildSessionInfoEvent(PermissionChecker? checker, IFastModeService? fastMode) {
        var version = typeof(EngineSessionFactory).Assembly.GetName().Version is { } v ? v.ToString(3) : "0.0.0";
        var mode = checker?.CurrentMode ?? EnginePermissionMode.Auto;
        return new BridgeEvent.SessionInfo(
            Version: version,
            Model: string.IsNullOrWhiteSpace(fastMode?.PrimaryModelId) ? null : fastMode.PrimaryModelId,
            PermissionMode: EnginePermissionModeExtensions.ToValue(mode),
            BypassAvailable: checker?.DisableBypassGate is not true,
            Cwd: Environment.CurrentDirectory);
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

    /// <summary>执行单个回合 — SessionTurnResult 翻译为 EngineTurnClient 异常语义；
    /// PermissionPendingConfirmationException（无 UI 确认）按打断处理并携带诱导说明。</summary>
    private static async Task RunTurnAsync(SessionController controller, string input, CancellationToken ct) {
        try {
            var result = await controller.StreamResponseAsync(input, ct).ConfigureAwait(false);
            var ex = TranslateTurnResult(result);
            if (ex is not null)
                throw ex;
        } catch (PermissionPendingException) {
            throw TranslatePendingPermission();
        }
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

    /// <summary>权限挂起翻译 — Host 首版按打断处理并携带诱导说明（M7 权限对话框时扩展为交互式确认）。</summary>
    private static OperationCanceledException TranslatePendingPermission() =>
        new("回合等待权限确认，已按打断处理。当前版本暂不支持交互式权限确认，请调整工具白名单或按 Shift+Tab 切换权限模式后重新提交。");

    /// <summary>释放 — 取消在途回合（client.Dispose 快速返回）+ 异步释放引擎 Host。</summary>
    public async ValueTask DisposeAsync() {
        _client.Dispose();
        await _engine.Host.DisposeAsync().ConfigureAwait(false);
    }
}
