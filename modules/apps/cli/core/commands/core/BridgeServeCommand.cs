namespace JoinCode.CliCommands;

/// <summary>
/// bridge_serve 元命令 — 启动 bridge 引擎服务端（ADR 0141 stdio NDJSON 协议）。
/// <para>组装胶水：EngineSessionFactory 建引擎会话 → SessionController 回合驱动（复用 EngineAgentFactory.CreateMainAgent）
/// → BridgeServeLoop 协议主循环。协议走 Console.OpenStandardOutput 独立 writer，
/// Console.Out 全程重定向 stderr 防引擎内部输出污染协议流。</para>
/// <para>消费方：TUI 进程拉起本命令（native_tui BridgeClient --bridge jcc.exe）。</para>
/// </summary>
internal static class BridgeServeCommand {
    /// <summary>
    /// 执行 bridge_serve — [--vendor V] [--model M]。
    /// </summary>
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct) {
        var unknownError = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames);
        if (unknownError is not null) {
            TerminalHelper.WriteError(unknownError);
            return 1;
        }
        var vendor = FlatSubCommandRouter.GetOptionValue(args, JccCliArgEnumConstants.Vendor);
        var model = FlatSubCommandRouter.GetOptionValue(args, JccCliArgEnumConstants.Model);
        // --profile 在子命令后仍须生效 — 子命令路由优先于全局 ParseArgs（Program.FindSubCommandIndex），
        // 引擎装配的 profile 由 options.Profile 决定（CoreModule → AddDataDrivenPipelines），
        // 与 --vendor/--model 同构：不解析则 `jcc bridge_serve --profile X` 静默回退 base（Wq01 红测试暴露）。
        var profile = FlatSubCommandRouter.GetOptionValue(args, JccCliArgEnumConstants.Profile);
        return await ExecuteBridgeAsync(vendor, model, profile, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 组装引擎会话并运行协议主循环 — stdin/stdout 接 OpenStandard 流（UTF-8），
    /// 全程持有 JCC_LOG_LEVEL=Error 抑制日志噪音。
    /// 启动即发 session_info 握手（版本/当前模型/权限模式/bypass 可用性/cwd — banner 真实数据源）。
    /// </summary>
    private static async Task<int> ExecuteBridgeAsync(string? vendor, string? model, string? profile, CancellationToken ct) {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var options = new CommandLineOptions { NonInteractive = true, TrustWorkspace = true, SkipModelFetch = true, SkipProviderValidation = true };
        if (!string.IsNullOrEmpty(vendor))
            options = options with { Vendor = vendor };
        if (!string.IsNullOrEmpty(model))
            options = options with { Model = model };
        if (!string.IsNullOrEmpty(profile))
            options = options with { Profile = profile };
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");

        var engine = await CliSessionFactory.CreateCliSessionAsync(options, fs, ct).ConfigureAwait(false);
        await using var host = engine.Host;

        // Console.Out → stderr：引擎内部任何 Console 输出不得污染 stdout 协议流
        // （协议事件走下方 OpenStandardOutput 独立 writer，不受 SetOut 影响）
        var originalOut = Console.Out;
        Console.SetOut(Console.Error);
        try {
            using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };

            var permissionChecker = await PermissionModeApplierFactory.ResolveAsync(engine.Services).ConfigureAwait(false);
            var fastMode = engine.Services.GetService<IFastModeService>();
            WriteEvent(stdout, BuildSessionInfo(permissionChecker, fastMode, options));

            // bridge-serve 需要完整聊天服务 — SkipChatServices 模式下 ChatService 为 null，直接报错退出
            if (engine.ChatService is null)
                return 1;
            var chatService = engine.ChatService;

            // M8-8 后台任务事件泵 — 订阅 ForkCompleted/TeammateCompleted 直推 consumer（绕开回合作用域通道），
            // 每回合终态后全量快照 upsert；两查询面均可选（引擎未注册时泵为 null，零行为变化）
            var backgroundPump = CreateBackgroundTaskPump(engine.Services);

            // ADR 0165 / TASK136 — 会话级 ObjectId 共享，**每查询新建 SessionController + 主代理**：
            // 回合级状态（_hasSubstantiveOutput/_lastStopReason/_turnToolCalls 等）不得跨查询共享，
            // 否则并发查询会互相污染终止判定；consumer 由 loop 按查询新建（回合内状态不串台）。
            // SessionStart hook 标记走容器单例（ISessionStartHookState），仍是会话级一次性语义。
            var sessionObjectId = new Session().ObjectId;

            return await BridgeServeLoop.RunAsync(stdin, stdout, Console.Error, (input, consumer, turnCt) => {
                var controller = new SessionController(
                    chatService, consumer, new TurnDiffService(), engine.SessionId, engine.Services,
                    mainAgent: EngineAgentFactory.CreateMainAgent(engine.Services, sessionObjectId));
                return RunTurnAsync(controller, input, turnCt);
            }, ct, PermissionModeApplierFactory.Create(permissionChecker), backgroundPump).ConfigureAwait(false);
        } finally {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>构造后台任务事件泵（M8-8）— 解析 fork/teammate 查询面（均引擎单例注册，可选），
    /// 任一可用即建泵；两者皆缺返回 null（协议无 background_task 事件流，TUI 面板空置）。</summary>
    private static BackgroundTaskEventPump? CreateBackgroundTaskPump(IServiceProvider services) {
        var forkManager = services.GetService<IForkSubAgentManager>();
        var teammateExecutor = services.GetService<IInProcessTeammateTaskExecutor>();
        return forkManager is not null || teammateExecutor is not null
            ? new BackgroundTaskEventPump(forkManager, teammateExecutor)
            : null;
    }

    /// <summary>
    /// 组装会话握手事件 — 版本（引擎程序集）/当前模型（FastMode 主模型，未设置回退 --model 参数，仍空为 null）/
    /// 权限模式（含安全闸后的实际值）/bypass 可用性（!DisableBypassGate）/cwd。
    /// </summary>
    internal static Cli.Bridge.BridgeEvent.SessionInfo BuildSessionInfo(
        PermissionChecker? checker,
        IFastModeService? fastMode,
        CommandLineOptions options) {
        var version = typeof(BridgeServeCommand).Assembly.GetName().Version is { } v ? v.ToString(3) : "0.0.0";
        var currentModel = !string.IsNullOrWhiteSpace(fastMode?.PrimaryModelId)
            ? fastMode.PrimaryModelId
            : string.IsNullOrWhiteSpace(options.Model) ? null : options.Model;
        var mode = checker?.CurrentMode ?? PermissionMode.Auto;
        return new Cli.Bridge.BridgeEvent.SessionInfo(
            Version: version,
            Model: currentModel,
            PermissionMode: mode.ToValue(),
            BypassAvailable: checker?.DisableBypassGate is not true,
            Cwd: Environment.CurrentDirectory);
    }

    /// <summary>写一条协议事件行（单次 Write + LF + Flush，协议统一 \n）。</summary>
    private static void WriteEvent(TextWriter stdout, Cli.Bridge.BridgeEvent bridgeEvent) {
        stdout.Write(Cli.Bridge.BridgeProtocol.SerializeEvent(bridgeEvent) + "\n");
        stdout.Flush();
    }

    /// <summary>
    /// 执行单个回合 — SessionTurnResult 翻译为 BridgeServeLoop 异常语义。
    /// 权限确认已改 ConfirmAsync 异步等待（ADR 0142）：NonInteractive 下 CLI 处理器自动 Deny，
    /// 工具拒绝走回合失败 → error 事件（消息含引擎诱导说明），无抛异常重试路径。
    /// </summary>
    private static async Task RunTurnAsync(SessionController controller, string input, CancellationToken ct) {
        var result = await controller.StreamResponseAsync(input, ct).ConfigureAwait(false);
        var ex = TranslateTurnResult(result);
        if (ex is not null)
            throw ex;
    }

    /// <summary>
    /// 翻译轮次结果 — 成功=null（loop 发 turn_completed）；取消=OperationCanceledException（turn_interrupted）；
    /// 超时/错误=InvalidOperationException（loop 发 error 事件，消息含诱导说明）。
    /// </summary>
    internal static Exception? TranslateTurnResult(SessionTurnResult result) {
        if (result.Succeeded)
            return null;
        if (result.WasCancelled)
            return new OperationCanceledException();
        if (result.TimedOut)
            return new InvalidOperationException(
                $"API 请求超时（{result.TimeoutMs}ms 无响应）。可发送 interrupt 打断后重试，或检查网络连接与 API 服务状态。");
        return new InvalidOperationException(result.ErrorMessage ?? "回合执行失败（未知错误）");
    }
}
