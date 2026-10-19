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
        return await ExecuteBridgeAsync(vendor, model, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 组装引擎会话并运行协议主循环 — stdin/stdout 接 OpenStandard 流（UTF-8），
    /// 全程持有 JCC_LOG_LEVEL=Error 抑制日志噪音。
    /// 启动即发 session_info 握手（版本/当前模型/权限模式/bypass 可用性/cwd — banner 真实数据源）。
    /// </summary>
    private static async Task<int> ExecuteBridgeAsync(string? vendor, string? model, CancellationToken ct) {
        var fs = IO.FileSystem.FileSystemFactory.Create();
        var options = new CommandLineOptions { NonInteractive = true, TrustWorkspace = true, SkipModelFetch = true, SkipProviderValidation = true };
        if (!string.IsNullOrEmpty(vendor))
            options = options with { Vendor = vendor };
        if (!string.IsNullOrEmpty(model))
            options = options with { Model = model };
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");

        var engine = await EngineSessionFactory.CreateCliSessionAsync(options, fs, ct).ConfigureAwait(false);
        await using var host = engine.Host;

        // Console.Out → stderr：引擎内部任何 Console 输出不得污染 stdout 协议流
        // （协议事件走下方 OpenStandardOutput 独立 writer，不受 SetOut 影响）
        var originalOut = Console.Out;
        Console.SetOut(Console.Error);
        try {
            using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };

            var permissionChecker = await ResolvePermissionCheckerAsync(engine.Services).ConfigureAwait(false);
            var fastMode = engine.Services.GetService<IFastModeService>();
            WriteEvent(stdout, BuildSessionInfo(permissionChecker, fastMode, options));

            var mainAgent = EngineAgentFactory.CreateMainAgent(engine.Services, new Session().ObjectId);
            SessionController? controller = null;

            return await BridgeServeLoop.RunAsync(stdin, stdout, Console.Error, (input, consumer, turnCt) => {
                // 首次 submit 时绑定回合驱动器 — consumer 即协议事件出口（loop 构造，全程同一实例）
                controller ??= new SessionController(
                    engine.ChatService, consumer, new TurnDiffService(), engine.SessionId, engine.Services,
                    mainAgent: mainAgent);
                return RunTurnAsync(controller, input, turnCt);
            }, ct, BuildPermissionModeApplier(permissionChecker)).ConfigureAwait(false);
        } finally {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>解析引擎权限检查器（等模式/安全闸初始化完成；未注册返回 null — 协议降级为仅展示默认值）。</summary>
    private static async Task<PermissionChecker?> ResolvePermissionCheckerAsync(IServiceProvider services) {
        if (services.GetService<IPermissionChecker>() is not PermissionChecker checker)
            return null;

        await checker.ModeInitialization.ConfigureAwait(false);
        return checker;
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

    /// <summary>
    /// 构造权限模式应用器 — 解析协议字符串（FromValue）→ 应用（CurrentMode setter 含 bypass 安全闸）→
    /// 返回实际生效模式（读回 CurrentMode，被闸拦截时与请求值不同）；未知值/无权限系统返回 null。
    /// </summary>
    private static BridgeServeLoop.PermissionModeApplier? BuildPermissionModeApplier(PermissionChecker? checker) {
        if (checker is null)
            return null;

        return mode => {
            var parsed = PermissionModeExtensions.FromValue(mode);
            if (parsed is null)
                return null;

            checker.CurrentMode = parsed.Value;
            return checker.CurrentMode;
        };
    }

    /// <summary>写一条协议事件行（单次 Write + LF + Flush，协议统一 \n）。</summary>
    private static void WriteEvent(TextWriter stdout, Cli.Bridge.BridgeEvent bridgeEvent) {
        stdout.Write(Cli.Bridge.BridgeProtocol.SerializeEvent(bridgeEvent) + "\n");
        stdout.Flush();
    }

    /// <summary>
    /// 执行单个回合 — SessionTurnResult 翻译为 BridgeServeLoop 异常语义；
    /// PermissionPendingConfirmationException（无 UI 确认）按打断处理并携带诱导说明。
    /// </summary>
    private static async Task RunTurnAsync(SessionController controller, string input, CancellationToken ct) {
        try {
            var result = await controller.StreamResponseAsync(input, ct).ConfigureAwait(false);
            var ex = TranslateTurnResult(result);
            if (ex is not null)
                throw ex;
        } catch (PermissionPendingConfirmationException) {
            throw TranslatePendingPermission();
        }
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

    /// <summary>
    /// 权限挂起翻译 — bridge 协议暂无交互式确认事件，按打断处理并携带诱导说明（M7 权限对话框时扩展协议）。
    /// </summary>
    internal static OperationCanceledException TranslatePendingPermission() =>
        new("回合等待权限确认，已按打断处理。bridge 协议暂不支持交互式权限确认，请调整工具白名单或权限模式后重新提交。");
}
