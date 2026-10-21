namespace NativeTui.Host;

/// <summary>
/// NativeTui 宿主入口 — ADR 0143 阶段二：进程内直连 jcc 引擎（IEventConsumer 事件直喂 ReplView，零协议税）。
/// --selftest：非交互冒烟（真实引擎组装 + session_info 握手 + 权限模式真实回执，不调 LLM）；
/// 交互模式（默认）：raw mode + 引擎会话组装 + inline 渲染主循环
/// （Enter 提交 / Esc 忙时打断或闲时退出 / Shift+Tab 权限循环 / Ctrl+C 退出）。
/// </summary>
public static class Program {
    /// <summary>入口 — --selftest 非交互冒烟；--selftest-turn 真实回合计时探针（诊断用）；
    /// 无参数进入交互 REPL；未知参数报错诱导。
    /// 顶层 catch ConfigurationException — 配置错误友好呈现（两级 profile 覆盖链 + auth key 状态 + 修复指引，
    /// 修复：配置错误直接闪退刷未处理异常堆栈），退出码 2。</summary>
    public static async Task<int> Main(string[] args) {
        try {
            if (args is ["--selftest"])
                return await RunSelftestAsync().ConfigureAwait(false);
            if (args is ["--selftest-turn"])
                return await RunSelftestTurnAsync().ConfigureAwait(false);
            if (args.Length > 0) {
                Console.Error.WriteLine($"未知参数: {string.Join(' ', args)}。用法: NativeTui.Host [--selftest | --selftest-turn]（无参数进入交互模式，需真实终端）");
                return 1;
            }

            return await RunInteractiveAsync().ConfigureAwait(false);
        } catch (ConfigurationException ex) {
            var vendor = StartupErrorPrinter.ExtractVendor(ex.Message) ?? "未知";
            Console.Error.WriteLine(StartupErrorPrinter.Format(ex, StartupErrorPrinter.ProbeLive(vendor)));
            return 2;
        }
    }

    /// <summary>真实回合计时探针（ADR 0080 — 诊断 TUI 链路回复延迟）— 提交一个真实回合
    /// （走用户交互同链：EngineTurnClient → SessionController(mainAgent) → QueryEngine），
    /// 逐事件打印相对时间戳（turn_started/首思考/首 token/完成/错误），60s 超时兜底。</summary>
    private static async Task<int> RunSelftestTurnAsync() {
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");
        TestEnvironmentDetector.ForceNonInteractive = true;
        await using var session = await EngineSession.CreateAsync().ConfigureAwait(false);
        _ = session.Client.DrainEvents();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        session.Client.Submit("只回复两个字母: ok");
        Console.WriteLine($"[probe] t={sw.ElapsedMilliseconds}ms 提交 /btw");

        string? firstThinking = null;
        string? firstToken = null;
        var tokenTotal = 0;
        var assistantMessages = new List<string>();
        while (sw.ElapsedMilliseconds < 60_000) {
            await Task.Delay(100).ConfigureAwait(false);
            foreach (var evt in session.Client.DrainEvents()) {
                switch (evt) {
                    case BridgeEvent.TurnStarted:
                        Console.WriteLine($"[probe] t={sw.ElapsedMilliseconds}ms turn_started");
                        break;
                    case BridgeEvent.ThinkingStarted:
                        if (firstThinking is null) {
                            firstThinking = $"{sw.ElapsedMilliseconds}ms";
                            Console.WriteLine($"[probe] t={firstThinking} 首思考");
                        }
                        break;
                    case BridgeEvent.TokenDelta(var delta, _):
                        if (firstToken is null) {
                            firstToken = $"{sw.ElapsedMilliseconds}ms";
                            Console.WriteLine($"[probe] t={firstToken} 首 token");
                        }
                        tokenTotal += delta;
                        break;
                    case BridgeEvent.AssistantMessage(var text, var markdown, var msgId, var thinkMs, var cot):
                        assistantMessages.Add(text);
                        Console.WriteLine($"[probe] t={sw.ElapsedMilliseconds}ms assistant_message #{assistantMessages.Count} | id={msgId} thinkMs={thinkMs} cotLen={cot?.Length ?? 0} | textLen={text.Length} markdown={(markdown is not null ? "有" : "null")} | 首部: {text[..Math.Min(60, text.Length)]}");
                        break;
                    case BridgeEvent.TurnCompleted:
                        Console.WriteLine($"[probe] t={sw.ElapsedMilliseconds}ms turn_completed | 首思考={firstThinking} 首 token={firstToken} | token总长={tokenTotal} | assistant_message数={assistantMessages.Count}");
                        foreach (var (msg, idx) in assistantMessages.Select((m, i) => (m, i + 1)))
                            Console.WriteLine($"[probe] 消息#{idx} 长度={msg.Length} 双倍检测={(msg.Length > 2 && msg[..(msg.Length / 2)] == msg[(msg.Length / 2)..] ? "前半==后半（内容双倍）" : "无")}");
                        return 0;
                    case BridgeEvent.Error(var message):
                        Console.WriteLine($"[probe] t={sw.ElapsedMilliseconds}ms ERROR: {message}");
                        return 1;
                }
            }
        }

        Console.WriteLine($"[probe] t={sw.ElapsedMilliseconds}ms 超时（60s 无 turn_completed）| 首思考={firstThinking} 首 token={firstToken}");
        return 1;
    }

    /// <summary>等待当前回合终态（turn_completed/interrupted/error — 排空事件循环）。</summary>
    private static async Task WaitTurnCompleteAsync(EngineSession session, TimeSpan timeout) {
        using var cts = new CancellationTokenSource(timeout);
        try {
            while (!cts.Token.IsCancellationRequested) {
                await Task.Delay(100, cts.Token).ConfigureAwait(false);
                foreach (var evt in session.Client.DrainEvents()) {
                    if (evt is BridgeEvent.TurnCompleted or BridgeEvent.TurnInterrupted or BridgeEvent.Error) {
                        return;
                    }
                }
            }
        } catch (OperationCanceledException) {
        }
    }

    /// <summary>交互模式 — TTY 检查 → UTF-8 → raw mode → 引擎组装（Console.Out 重定向 stderr 防引擎污染渲染）→ REPL 主循环。</summary>
    private static async Task<int> RunInteractiveAsync() {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) {
            Console.Error.WriteLine("交互模式需要真实终端（stdin/stdout 被重定向）。请直接在终端运行 NativeTui.Host，或用 --selftest 跑非交互冒烟。");
            return 1;
        }

        Console.OutputEncoding = Encoding.UTF8;
        using var raw = new RawConsoleMode();
        Console.Error.WriteLine("引擎启动中…");
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");
        TestEnvironmentDetector.ForceNonInteractive = true;

        // Console.Out → stderr：引擎内部任何 Console 输出不得污染 TUI 渲染流
        // （TUI 渲染走重定向前捕获的独立 writer，不受 SetOut 影响 — 对齐 BridgeServeCommand 先例）
        var tuiOutput = Console.Out;
        var originalOut = Console.Out;
        Console.SetOut(Console.Error);
        try {
            EngineSession session;
            try {
                session = await EngineSession.CreateAsync().ConfigureAwait(false);
            } catch (ConfigurationException) {
                throw; // 冒泡 Main 统一友好呈现（raw mode 由 using 恢复后再打印）
            } catch (Exception ex) {
                Console.Error.WriteLine($"引擎启动失败: {ex.Message}。请检查 jcc 引擎配置（settings.json / providers）后重试。");
                return 1;
            }

            await using (session) {
                // banner 占位值 — 首拍 session_info 种子事件覆盖为引擎真实数据（EngineSession.Enqueue）
                var banner = new BannerItem("0.0.0", "当前模型未设置", "API 按量计费", Environment.CurrentDirectory);
                var commands = SlashCommandSource.Load(session.Services);
                // M7-6 模型选择器 — 装饰引擎拦截 /model（清单从引擎 IModelCatalog 后端获取，切换链自研）；
                // 切换成功后 RefreshSessionInfo 重发握手事件刷新 banner（当前模型显示同步）
                // TASK058 供应商选择器 — 外层再装饰拦截 /vendor（清单 VendorKind 枚举投影，切换链对齐 GUI SetVendorAsync）
                var pickerEngine = new CommandPickerEngine(
                    new CommandPickerEngine(
                        session.Client,
                        "/model",
                        "模型",
                        () => ModelPickerAssembly.LoadItems(session.Services),
                        id => {
                            ModelPickerAssembly.ApplyModel(session.Services, id);
                            session.RefreshSessionInfo();
                        }),
                    "/vendor",
                    "供应商",
                    () => VendorPickerAssembly.LoadItems(session.Services),
                    vendor => {
                        VendorPickerAssembly.ApplyVendor(session.Services, vendor);
                        session.RefreshSessionInfo();
                    });
                // TASK059 退出安全 — Esc 永不退出（忙时双击中断）、Ctrl+C 复制最后 assistant 消息；
                // 退出路径：Ctrl+D（空输入）或 /exit 命令。剪贴板走引擎 IClipboardService（fire-and-forget 写入）。
                var clipboardService = session.Services.GetService<IClipboardService>();
                var shell = new ReplShell(pickerEngine, banner, tuiOutput, commands: commands,
                    clipboard: text => _ = clipboardService?.SetTextAsync(text));
                pickerEngine.Attach(shell);
                await HistorySeeder.SeedAsync(shell, session.Services, session.SessionId).ConfigureAwait(false);
                session.PermissionBridge.Bind(shell);
                // M8-5 命令交互回调接线（/clear 清 TUI 滚动区+scrollback、/exit 结束主循环）—
                // ReplShell 公开入口已由主智能体收编落地（ClearMessagesExceptBannerAndSpinner/RequestExit）
                session.CommandCallbacks.Bind(
                    clearScreen: shell.ClearMessagesExceptBannerAndSpinner,
                    onExitRequested: shell.RequestExit);
                shell.Run();
                shell.WriteExit();
            }
        } finally {
            Console.SetOut(originalOut);
        }

        Console.WriteLine("NativeTui.Host 已退出。");
        return 0;
    }

    /// <summary>非交互冒烟（ADR 0080 真实组装非 mock）— 真实引擎会话组装 + session_info 握手断言 +
    /// 权限模式真实回执（PermissionChecker 安全闸链路）+ 无效模式 Error 诱导；不提交回合（不调 LLM）。</summary>
    private static async Task<int> RunSelftestAsync() {
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");
        TestEnvironmentDetector.ForceNonInteractive = true;
        await using var session = await EngineSession.CreateAsync().ConfigureAwait(false);

        var failures = new List<string>();
        var events = session.Client.DrainEvents();
        AssertEvent(events, e => e is BridgeEvent.SessionInfo { Version.Length: > 0, PermissionMode.Length: > 0, Cwd.Length: > 0 }, "session_info 握手（版本/权限模式/cwd）", failures);

        session.Client.SetPermissionMode("plan");
        events = session.Client.DrainEvents();
        AssertEvent(events, e => e is BridgeEvent.PermissionModeChanged { Mode: "plan" }, "权限模式切换回执 plan（真实 PermissionChecker）", failures);

        session.Client.SetPermissionMode("no-such-mode");
        events = session.Client.DrainEvents();
        AssertEvent(events, e => e is BridgeEvent.Error err && err.Message.Contains("plan"), "无效模式 Error 诱导（含合法值提示）", failures);

        // M7-6 — 模型选择器后端获取链路：引擎 DI 必须可解析 IModelCatalog（GUI 会话模块链注册验证）
        var catalog = session.Services.GetService<IModelCatalog>();
        if (catalog is null)
            failures.Add("IModelCatalog 未在引擎 DI 注册（模型选择器将降级单条项）");
        else
            AssertModelItems(ModelPickerAssembly.LoadItems(session.Services), failures);

        // DSG041 集成验收第②项 — 子代理事件链冒烟（真实 spawn 路径；MockServer 未构建时跳过非失败）
        var subagentResult = await SubagentSelftest.RunAsync().ConfigureAwait(false);
        switch (subagentResult.Status) {
            case SubagentSelftest.Result.Failed:
                failures.AddRange(subagentResult.Failures);
                break;
            case SubagentSelftest.Result.Skipped:
                Console.WriteLine("SKIP: " + subagentResult.Failures[0]);
                break;
        }

        if (failures.Count > 0) {
            foreach (var failure in failures)
                Console.Error.WriteLine("FAIL: " + failure);
            return 1;
        }

        Console.WriteLine("HOST SELFTEST OK — 引擎进程内组装 + session_info 握手 + 权限模式真实回执 全部通过");
        return 0;
    }

    /// <summary>断言事件序列存在满足谓词的事件（失败记录收到的类型清单，便于定位）。</summary>
    private static void AssertEvent(IReadOnlyList<BridgeEvent> events, Func<BridgeEvent, bool> predicate, string what, List<string> failures) {
        if (!events.Any(predicate))
            failures.Add($"断言失败: {what}（收到：{string.Join(", ", events.Select(e => e.GetType().Name))}）");
    }

    /// <summary>断言模型选择器清单有效（M7-6 — 非空 + 含当前模型标记 + 显示名非空）。</summary>
    private static void AssertModelItems(IReadOnlyList<ModelPickerItem> items, List<string> failures) {
        if (items.Count == 0)
            failures.Add("模型清单为空（GetModelsForProvider 返回空且降级项未生效）");
        else if (items.All(m => !m.IsCurrent))
            failures.Add($"模型清单无当前模型标记（{items.Count} 项，EnsureCurrentModelInList 语义失效）");
        if (items.Any(m => string.IsNullOrWhiteSpace(m.DisplayName)))
            failures.Add("模型清单存在空 DisplayName");
    }
}
