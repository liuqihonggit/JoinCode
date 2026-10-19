namespace JoinCode.CliCommands;

/// <summary>
/// bridge_serve 元命令 — 启动 bridge 引擎服务端（ADR 0141 stdio NDJSON 协议）。
/// <para>组装胶水：EngineSessionFactory 建引擎会话 → SessionController 回合驱动（复用 CliSession.CreateMainAgent）
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

            var mainAgent = CliSession.CreateMainAgent(engine.ChatService, engine.Services, new Session().ObjectId);
            SessionController? controller = null;

            return await BridgeServeLoop.RunAsync(stdin, stdout, Console.Error, (input, consumer, turnCt) => {
                // 首次 submit 时绑定回合驱动器 — consumer 即协议事件出口（loop 构造，全程同一实例）
                controller ??= new SessionController(
                    engine.ChatService, consumer, new TurnDiffService(), engine.SessionId, engine.Services,
                    mainAgent: mainAgent);
                return RunTurnAsync(controller, input, turnCt);
            }, ct).ConfigureAwait(false);
        } finally {
            Console.SetOut(originalOut);
        }
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
