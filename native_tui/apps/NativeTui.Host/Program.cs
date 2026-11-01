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
            if (args is ["--selftest-commands"])
                return await RunSelftestCommandsAsync().ConfigureAwait(false);
            if (args is ["--raw-input"])
                return await RunInteractiveAsync(useRawStdinPump: true).ConfigureAwait(false);
            if (args is ["--selftest-input"])
                return await RunSelftestInputAsync().ConfigureAwait(false);
            if (args.Length > 0) {
                Console.Error.WriteLine($"未知参数: {string.Join(' ', args)}。用法: NativeTui.Host [--selftest | --selftest-turn | --selftest-commands | --raw-input]（无参数进入交互模式，需真实终端；--raw-input 启用 stdin 原始流输入泵灰度通道）");
                return 1;
            }

            return await RunInteractiveAsync().ConfigureAwait(false);
        } catch (ConfigurationException ex) {
            var vendor = StartupErrorPrinter.ExtractVendor(ex.Message) ?? "未知";
            Console.Error.WriteLine(StartupErrorPrinter.Format(ex, StartupErrorPrinter.ProbeLive(vendor)));
            return 2;
        }
    }

    /// <summary>斜杠命令批量探针（TASK065 — 52 条安全命令集走 TUI 真实链路逐条实测）：
    /// EngineTurnClient.Submit → DispatchSlashCommandAsync → SlashCommandRunner 真实执行，
    /// 逐命令分类 OK（有输出）/EMPTY（空回合）/ERROR（异常）/TIMEOUT，输出汇总表供人工核对。
    /// 排除副作用命令（reset-config/logout/clear/状态改写类）与调 LLM 命令（--selftest-turn 已覆盖）。
    /// 新增仅只读查询类+当前值显示类：无参查询态（/advisor /assistant /buddy /switch /sampling /statusline）、
    /// 默认 list 分支（/hooks /plugin /permissions /workflows /memory /skills）、显式查询分支（/mcp status /plan status /diag /falv）。</summary>
    private static async Task<int> RunSelftestCommandsAsync() {
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");
        TestEnvironmentDetector.ForceNonInteractive = true;
        await using var session = await EngineSession.CreateAsync().ConfigureAwait(false);
        _ = session.Client.DrainEvents();
        var exitRequests = new List<string>();
        session.CommandCallbacks.Bind(
            clearScreen: null,
            onExitRequested: () => exitRequests.Add("exit"));

        var probes = new (string Cmd, string Note)[] {
            ("/tools", "工具-首条时间锚点"),
            ("/mcp list", "MCP-首条时间锚点"),
            ("/mcp status", "MCP-连接状态查询"),
            ("/status", "综合状态"),
            ("/version", "版本"),
            ("/help", "帮助"),
            ("/context", "上下文"),
            ("/doctor", "环境诊断"),
            ("/env", "环境变量"),
            ("/usage", "用量"),
            ("/stats", "统计"),
            ("/cost today", "今日成本"),
            ("/config list", "配置清单"),
            ("/tasks", "任务"),
            ("/history", "历史"),
            ("/files", "文件"),
            ("/tag list", "标签"),
            ("/trust list", "信任"),
            ("/privacy-settings show", "隐私"),
            ("/theme", "主题当前值"),
            ("/effort", "推理力度当前值"),
            ("/sampling", "采样参数当前值"),
            ("/vendor", "供应商引擎路径"),
            ("/model info", "模型信息"),
            ("/agents", "代理列表"),
            ("/advisor", "顾问模式当前态"),
            ("/assistant", "助手模式当前态"),
            ("/buddy", "伙伴精灵信息"),
            ("/switch", "输出显示模式当前态"),
            ("/memory", "记忆文件列表"),
            ("/plan status", "计划模式状态查询"),
            ("/summary", "摘要"),
            ("/worktree list", "工作树"),
            ("/branch list", "分支列表"),
            ("/session list", "会话列表"),
            ("/diff", "未提交变更"),
            ("/skills", "自定义技能列表"),
            ("/hooks", "Hook配置列表"),
            ("/plugin", "插件列表"),
            ("/permissions", "权限规则列表"),
            ("/workflows", "工作流列表"),
            ("/debuglog", "调试日志状态"),
            ("/statusline", "状态栏当前配置"),
            ("/diag", "崩溃快照查询"),
            ("/heapdump", "运行时诊断查询"),
            ("/falv", "推理引擎帮助"),
            ("/peers", "对等节点列表"),
            ("/quit", "退出类-直接"),
            ("/exit", "退出类-确认"),
            ("/nosuchcmd", "未知命令"),
            ("/?", "别名-help"),
            ("/q", "别名-quit"),
        };

        var results = new List<(string Cmd, string Note, string Verdict, string Detail)>();
        foreach (var (cmd, note) in probes) {
            var (verdict, detail) = await ProbeOneCommandAsync(session, cmd).ConfigureAwait(false);
            // 退出类命令（/quit /q 无确认直接退 — 期望 ExitRequested 钩子触发；
            // /exit 走 S8 confirm 闭环 — 探针未 Bind 权限 UI 时 bridge 自动 Deny → 不退出是预期降级
            //（确认拒绝语义），两类都算链路通）
            if (cmd is "/quit" or "/exit" or "/q") {
                if (exitRequests.Count > 0) {
                    verdict = "EXIT-OK";
                    detail = $"ExitRequested={string.Join(",", exitRequests)}";
                } else if (cmd == "/exit") {
                    verdict = "OK";
                    detail = "confirm Deny 降级（无 UI 宿主预期行为 — 确认拒绝不退出）";
                }

                exitRequests.Clear();
            }
            results.Add((cmd, note, verdict, detail));
            Console.WriteLine($"[cmd] {verdict,-7} {cmd,-24} {note,-14} {detail}");
        }

        Console.WriteLine();
        Console.WriteLine($"[cmd] 汇总: 总数={results.Count} OK={results.Count(r => r.Verdict == "OK")} EMPTY={results.Count(r => r.Verdict == "EMPTY")} ERROR={results.Count(r => r.Verdict == "ERROR")} TIMEOUT={results.Count(r => r.Verdict == "TIMEOUT")}");
        var failures = results.Where(r => r.Verdict is "EMPTY" or "ERROR" or "TIMEOUT").ToList();
        if (failures.Count > 0) {
            Console.WriteLine("[cmd] 非全 OK 清单（供修复登记）:");
            foreach (var f in failures)
                Console.WriteLine($"[cmd]   {f.Verdict,-7} {f.Cmd,-24} {f.Detail}");
            return 1;
        }

        Console.WriteLine("[cmd] COMMANDS SELFTEST OK — 全部命令有输出且无异常");
        return 0;
    }

    /// <summary>探单命令 — 提交 → 等终态 → 分类（OK=有 assistant 输出 / EMPTY=空回合 /
    /// ERROR=Error 事件 / TIMEOUT=超时未终态）；detail 附输出长度与首部预览。</summary>
    private static async Task<(string Verdict, string Detail)> ProbeOneCommandAsync(EngineSession session, string cmd) {
        session.Client.Submit(cmd);
        var texts = new List<string>();
        string? error = null;
        var finished = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try {
            while (!cts.Token.IsCancellationRequested && !finished) {
                await Task.Delay(100, cts.Token).ConfigureAwait(false);
                foreach (var evt in session.Client.DrainEvents()) {
                    switch (evt) {
                        case BridgeEvent.AssistantMessage(var text, _, _, _, _):
                            texts.Add(text);
                            break;
                        case BridgeEvent.Error(var message):
                            error = message;
                            finished = true;
                            break;
                        case BridgeEvent.TurnCompleted or BridgeEvent.TurnInterrupted:
                            finished = true;
                            break;
                    }
                }
            }
        } catch (OperationCanceledException) {
        }

        if (error is not null)
            return ("ERROR", Truncate(error, 70));
        if (!finished)
            return ("TIMEOUT", "25s 无终态");
        var joined = string.Join("\n", texts).Trim();
        if (joined.Length == 0)
            return ("EMPTY", "回合完成但零输出（断层疑似：退出类/交互类降级）");
        return ("OK", $"len={joined.Length} | {Truncate(joined.Replace("\n", "⏎"), 50)}");
    }

    /// <summary>截断预览（单行化）。</summary>
    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

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

    /// <summary>交互模式 — TTY 检查 → UTF-8 → raw mode → 引擎组装（Console.Out 重定向 stderr 防引擎污染渲染）→ REPL 主循环。
    /// ADR 0149：改为 public 供 jcc 宿主默认调用（NativeTui.Host 已改为 Library）。</summary>
    public static async Task<int> RunInteractiveAsync(bool useRawStdinPump = false) {
        var probeSw = System.Diagnostics.Stopwatch.StartNew();
        var probeLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jcc_startup_timing.txt");
        void Probe(string label) => System.IO.File.AppendAllText(probeLog, $"[probe] t={probeSw.ElapsedMilliseconds}ms {label}\n");
        Probe("RunInteractiveAsync 入口");

        if (Environment.GetEnvironmentVariable("JCC_FORCE_TTY") != "1" && (Console.IsInputRedirected || Console.IsOutputRedirected)) {
            Console.Error.WriteLine("交互模式需要真实终端（stdin/stdout 被重定向）。请直接在终端运行 jcc，或用 --selftest 跑非交互冒烟。");
            return 1;
        }

        Console.OutputEncoding = Encoding.UTF8;
        using var raw = (Environment.GetEnvironmentVariable("JCC_FORCE_TTY") == "1" ? (IDisposable?)null : new RawConsoleMode());
        // TASK077 — 启动诊断行保存（纫入 TUI 渲染 — 应用内选区覆盖，不写 Console.Error 避免 scrollback 重复）
        var startupLines = new List<string>();
        void WriteStartupLine(string line)
        {
            foreach (var part in line.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                startupLines.Add(part);
            }
        }

        WriteStartupLine("引擎启动中…");
        // S12 OSC8 最小集成 — 会话级探测一次（stderr 诊断旁路 — 对齐 DrainDiagnostics 写 stderr 先例）：
        // 探测结果缓存于日志供链接渲染深链路批次消费（本批仅探测 + 说明，见未决问题）
        // 诊断行附带环境快照（TERM_PROGRAM/LC_TERMINAL/WT_SESSION/TERM/KITTY_WINDOW_ID）—
        // 用户贴报问题时 AI 一眼看清终端环境，无需另跑命令排查
        WriteStartupLine(HyperlinkEmitter.FormatDiagnostic());
        // 开关状态诊断（AGENTS.md 开关优先 + 助记符 — 启动 banner 列出当前开关状态）
        WriteStartupLine($"[diag] 开关: MouseTracking={JccFeatureFlags.MouseTrackingEnabled} Hover={JccFeatureFlags.HoverHighlightEnabled} ClickExpand={JccFeatureFlags.ClickExpandEnabled} (JCC_NO_MOUSE_TRACKING={Environment.GetEnvironmentVariable("JCC_NO_MOUSE_TRACKING") ?? "(未设置)"})");
        Probe("WriteStartupLine 完成");
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");
        TestEnvironmentDetector.ForceNonInteractive = true;

        // TUI 渲染独占 writer — 闸门换装前捕获（换装后 Console.Out 是环形缓冲 writer，不可用作渲染通道）
        var tuiOutput = Console.Out;
        // TASK077 控制台抑制闸门 — 会话期 Console.Out/Error 全部导入环形缓冲（替换旧 SetOut(Console.Error)
        // 拦截：stderr 在真实终端与 TUI 同一画布，[DIAG-ERR] 等诊断行照旧污染渲染流 — 底行溢出滚屏打垮
        // InlineComposer 相对光标模型，整帧永久失同步）；引擎杂散输出/Diag/McpPluginBootstrap 一律不再
        // 触碰终端，最外层退出时冲刷回原 stderr（CI/E2E 诊断捕获不受损）
        using var consoleSuppression = ConsoleSuppressionGate.Begin("TUI 会话");
        Probe("ConsoleSuppressionGate.Begin 完成");
        // E1 崩溃兜底 — CursorLifecycle 注释契约兑现：raw mode 运行期内任何未处理异常/信号退出路径，
        // 钩子统一补发硬件光标 Show + bracketed paste Disable（using RawConsoleMode 只恢复 console mode，
        // 覆盖不了 DEC 25/2004 两个终端层状态 — 对齐基准 ink.tsx signal-exit 恢复链）
        using var restoreGuard = new TerminalRestoreGuard(tuiOutput);
        Probe("TerminalRestoreGuard 完成");
        // earlyInput — 引擎组装窗口期按键捕获（基准 earlyInput.ts:29-67 — 启动期输入不丢失，
        // 就绪后回填输入框；非阻塞 KeyAvailable 轮询，无按键零耗时）
        var earlyKeys = Environment.GetEnvironmentVariable("JCC_FORCE_TTY") == "1"
            ? new List<ConsoleKeyInfo>()
            : EarlyInputBuffer.CaptureAvailable(() => Console.KeyAvailable, Console.ReadKey);
        Probe("EarlyInputBuffer.CaptureAvailable 完成");
        // 终端查询路由器（TASK067 批4 — 探测链路地基：DA1/DECRPM/kitty flags/DSR/XTVERSION
        // 查询-应答 promise；RawStdinPump 前置分流路由；主动探测接入 TerminalCapabilities 为后续增量）
        using var terminalQuerier = new TerminalQuerier(tuiOutput);
        Probe("TerminalQuerier 构造完成");
        {
            EngineSession session;
            try {
                Probe("EngineSession.CreateAsync 前");
                session = await EngineSession.CreateAsync().ConfigureAwait(false);
                Probe("EngineSession.CreateAsync 完成");
            } catch (ConfigurationException) {
                throw; // 冒泡 Main 统一友好呈现（raw mode 由 using 恢复后再打印）
            } catch (Exception ex) {
                Console.Error.WriteLine($"引擎启动失败: {ex.Message}。请检查 jcc 引擎配置（settings.json / providers）后重试。");
                return 1;
            }

            await using (session) {
                // banner 占位值 — 首拍 session_info 种子事件覆盖为引擎真实数据（EngineSession.Enqueue）
                var banner = new BannerItem("0.0.0", "当前模型未设置", "API 按量计费", Environment.CurrentDirectory);
                var commands = ApplyPickerHints(SlashCommandSource.Load(session.Services));
                Probe("SlashCommandSource.Load 完成");
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
                        },
                        // TASK066 D5 — effort 通道接入引擎现有链（唯一数据源）：
                        // 会话档位种子投影（IExecutionSettingsProvider.EffortLevel，Auto → null）+
                        // 确认应用（执行设置写入 + settings.json 持久化 + max 降级 — 模型回调先行，
                        // effort 应用时 PrimaryModelId 已为确认选中的新模型）
                        () => ModelPickerAssembly.LoadEffortConfig(session.Services),
                        level => ModelPickerAssembly.ApplyEffort(session.Services, level)),
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
                // S9/S10 接线 — 主题配置名（引擎 settings.json theme 键 → ThemeSelection.Resolve，
                // auto 按本地小时解析 light/dark，对齐引擎 ThemeCommand.ApplyTheme）+ 命令使用记忆
                //（~/.jcc/command-history.json 坏文件静默降级空记忆）+ 工作区根（@ 文件补全索引根，banner cwd 同源）
                var themeName = await LoadThemeNameAsync(session.Services).ConfigureAwait(false);
                Probe("LoadThemeNameAsync 完成");
                var commandUsage = await CommandUsageMemory.LoadAsync(CommandUsageMemory.DefaultFilePath).ConfigureAwait(false);
                Probe("CommandUsageMemory.LoadAsync 完成");
                // B类 editorMode 键接线 — settings.current.editorMode="vim" 持久启用 vim 编辑模式
                //（对齐 TS GlobalConfig.editorMode，经 IConfigurationService 读取 — theme 先例）；
                // JCC_TUI_VIM 灰度 OR 语义保留（EditorModeResolver 唯一判定源）
                var vimMode = await LoadVimModeAsync(session.Services).ConfigureAwait(false);
                Probe("LoadVimModeAsync 完成");
                // statusline 键激活 — statusline.enabled=true + statusline.format 模板
                //（/statusline 命令写入的键，theme 先例同模式读取）
                var statusLineTemplate = await LoadStatusLineTemplateAsync(session.Services).ConfigureAwait(false);
                Probe("LoadStatusLineTemplateAsync 完成");
                // C类 defaultShell 键 — settings.current.defaultShell（bash/powershell）覆盖平台默认
                var defaultShell = await LoadSettingAsync(session.Services, "defaultShell").ConfigureAwait(false);
                Probe("LoadSettingAsync(defaultShell) 完成");
                // C类 respectGitignore 键 — settings.current.respectGitignore（默认 true）控制 @ 文件补全是否尊重 .gitignore
                var gitignorePredicate = await LoadGitignorePredicateAsync(session.Services, Environment.CurrentDirectory).ConfigureAwait(false);
                Probe("LoadGitignorePredicateAsync 完成");
                // TASK077 — 输入泵平台分流：Windows 默认 RawStdinPump（stdin 原始流 VT 全链 —
                // ENABLE_VIRTUAL_TERMINAL_INPUT 启用后 WT 把 VT 鼠标序列发到 stdin，KeyPressParser
                // 解析 SGR 鼠标事件）；--raw-input 显式同路径；Linux 默认 null（ReplShell 回退
                // ReadKeyPump — Linux 终端 stdin VT 序列天然含鼠标）
                IInputPump? inputPump;
                Probe("inputPump 创建前");
                if (useRawStdinPump || OperatingSystem.IsWindows())
                {
                    inputPump = new RawStdinPump(terminalQuerier);
                }
                else
                {
                    inputPump = null;
                }
                // TASK066 板块A — 终端状态三路装配（标题/21337 tab/9;4 进度）：
                // 标题写通道平台分流（基准 use-terminal-title.ts：Windows=process.title → Console.Title，
                // 其余 OSC 0 经 TUI 输出流）；CLAUDE_CODE_DISABLE_TERMINAL_TITLE 对齐禁用；
                // tab 状态默认开 + JCC_TUI_TAB_STATUS=0 关闭；进度走能力判定（WT 排除/ConEmu 白名单）
                var titleTracker = new TerminalTitleTracker(
                    "JoinCode",
                    OperatingSystem.IsWindows()
                        ? title => Console.Title = title
                        : title => tuiOutput.Write(Osc.SetTitle(title)),
                    disabled: Environment.GetEnvironmentVariable("CLAUDE_CODE_DISABLE_TERMINAL_TITLE") is not null
                        and not ("0" or "false"));
                var tabStatusEnabled = Environment.GetEnvironmentVariable("JCC_TUI_TAB_STATUS") != "0";
                var terminalStatus = new TerminalStatusCoordinator(
                    tuiOutput, titleTracker, tabStatusEnabled,
                    progressEnabled: TerminalCapabilities.IsProgressReportingAvailable());
                // TASK066 板块B 连线B4 — 用户键位装配（~/.jcc/keybindings.json 覆盖默认表）：
                // 缺文件静默回退默认（ENOENT 语义）；坏条目尽力合并（合法条目生效、错误条目跳过）
                // + 校验错误 stderr 诊断不中断启动（对齐 command-history 坏文件降级 + 诊断先例）
                var keybindingLoad = KeybindingLoader.Load();
                Probe("KeybindingLoader.Load 完成");
                foreach (var keybindingError in keybindingLoad.Errors)
                {
                    Console.Error.WriteLine(keybindingError.ToString());
                }
                // TASK078 — TUI 原生需求澄清：/?? //ask 拦截为正常回合（CLI 版 AskClarifyCommand 是
                // 直写终端的交互循环，TUI 会话内不可见且 ReadLine 阻塞卡死）；提示词单一数据源 =
                // composition AskClarifyPrompts（宿主注入，本装饰器不依赖 composition）
                var clarifyEngine = new ClarifyModeEngine(pickerEngine, JoinCode.ChatCommands.AskClarifyPrompts.SystemPrompt);
                // TASK079 — 提示词透明化：/prompt 把首次注入的系统提示词全文 + 工具清单 dump 进
                // transcript（数据 = 引擎会话服务真实对象：ISystemPromptProvider sections +
                // IMcpToolRegistry 工具清单，单一数据源不硬编码；registry 异步预热缓存）
                var toolRegistry = session.Services.GetService<IMcpToolRegistry>();
                var promptProvider = session.Services.GetService<ISystemPromptProvider>();
                // TASK079 — 工具清单惰性构建（文本 + 统计：总数/完整注入数供折叠项摘要行展示）
                var toolInventoryCache = new Lazy<(string Text, int Total, int System)>(() =>
                {
                    if (toolRegistry is null)
                    {
                        return ("（工具注册表不可用）", 0, 0);
                    }

                    var tools = toolRegistry.GetAllToolsAsync().GetAwaiter().GetResult()
                        .OrderBy(t => t.Value.Category, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(t => t.Key, StringComparer.Ordinal)
                        .ToList();
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"共 {tools.Count} 个工具：");
                    string? lastCategory = null;
                    foreach (var (name, handler) in tools)
                    {
                        if (handler.Category != lastCategory)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"[{handler.Category ?? "其他"}]");
                            lastCategory = handler.Category;
                        }

                        sb.AppendLine($"  {name} ({handler.Kind.ToValue()}): {handler.Description}");
                    }

                    var systemCount = tools.Count(t => t.Value.Kind == ToolKind.System);
                    return (sb.ToString(), tools.Count, systemCount);
                }, LazyThreadSafetyMode.ExecutionAndPublication);
                var promptSnapshotBuilder = new Lazy<string>(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("【首次注入的系统提示词】");
                    if (promptProvider is not null)
                    {
                        foreach (var section in promptProvider.GetSections())
                        {
                            var content = section.ComputeValueTaskAsync().GetAwaiter().GetResult();
                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                sb.AppendLine($"── {section.Name} ──");
                                sb.AppendLine(content);
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine("（系统提示词提供器不可用）");
                    }

                    sb.AppendLine();
                    sb.AppendLine("【工具清单】");
                    sb.AppendLine(toolInventoryCache.Value.Text);
                    return sb.ToString();
                }, LazyThreadSafetyMode.ExecutionAndPublication);
                string? promptFileUrl = null;
string? promptFallbackPath = null;
try
{
    promptFallbackPath = Path.Combine(Path.GetTempPath(), $"jcc-prompt-{DateTime.Now:yyyyMMdd-HHmmss}.md");
    File.WriteAllText(promptFallbackPath, promptSnapshotBuilder.Value);
    promptFileUrl = new Uri(promptFallbackPath).AbsoluteUri;
}
catch (IOException) { }
catch (UnauthorizedAccessException) { }

var transparencyEngine = new PromptTransparencyEngine(clarifyEngine);
                Probe("transparencyEngine 创建完成");
                // TASK079 — bash/powershell 执行倒计时：超时秒数取 ShellExecutionConfig（会话服务，默认 120s）
                var shellExecutionConfig = session.Services.GetService<ShellExecutionConfig>();
                Probe("ReplShell 构造前");
                var shell = new ReplShell(transparencyEngine, banner, tuiOutput, commands: commands,
                    shellTimeoutSeconds: shellExecutionConfig?.AbsoluteTimeoutSeconds ?? 120,
                    promptContextSnapshot: promptSnapshotBuilder.Value,
                    promptContextInventory: () => toolInventoryCache.Value,
                    promptContextFileUrl: promptFileUrl,
                    clipboard: text => _ = clipboardService?.SetTextAsync(text),
                    themeName: themeName,
                    commandUsage: commandUsage,
                    workspaceRoot: Environment.CurrentDirectory,
                    // M8-8 后续 — x 停止后台任务委托：面板运行中条目 → 回合客户端停止入口（多态统一面 —
                    // 进程内 EngineTurnClient 泵分派 CancelFork/StopTeammate，bridge 形态走同接口的
                    // BridgeEngine 写 stop_task 请求；cancelled 终态 upsert 由泵推回面板即时反馈）
                    onStopTask: (kind, taskId) => session.Client.StopBackgroundTask(kind, taskId),
                    // 方案 B — 流式期 Markdown 块级增量投影（Markdig 卫星注入：稳定前缀 memoize +
                    // 增长尾纯文本，对齐基准 StreamingMarkdown；落定替换 MarkdownMessageItem 全量渲染）
                    streamingProjector: new StreamingMarkdownProjector(),
                    // S11 遗留#1 — /theme 持久化：合法切换写回引擎 settings.json theme 键
                    //（IConfigurationService.SetAsync；写盘失败静默降级会话级 — ReplShell 侧容错）
                    saveThemeAsync: name => SaveThemeAsync(session.Services, name),
                    // TASK064 P3 — ctrl+g 外部编辑器委托：输入文本 → temp 文件 → $EDITOR
                    //（未设置时 Windows notepad.exe / 其余 vi 兜底）→ 读回编辑后文本；失败 null 降级丢弃
                    launchExternalEditor: text => ExternalEditorLauncher.EditAsync(text),
                    // S12 四域接线 — /export 文件目标写盘委托：conversation-{时间戳}.md 写当前目录
                    //（同刻冲突递增后缀防覆盖；返回文件名供通知反馈，异常由 ReplShell 侧容错降级）
                    saveExportAsync: text => SaveConversationExportAsync(text),
                    // S12 四域接线 — ctrl+shift+f 全局搜索源：rg 执行器（PATH 解析，缺失时
                    // 对话框打开即降级提示；搜索根 = workspaceRoot 同一工作区根）
                    searchSource: new RipgrepSearcher(),
                    // DSG043 尾批 — issue 引用短形态上下文（git remote origin → owner/repo；
                    // 无 git/无 remote/非 GitHub = null 短形态不展开，完整形态恒展开）
                    issueOwnerRepo: await ResolveIssueOwnerRepoAsync().ConfigureAwait(false),
                    // DSG042 P2 — 输入泵通道：Windows 默认 Win32ConsoleInputPump（TASK077 鼠标支持）；
                    // --raw-input 灰度启用 RawStdinPump（stdin 原始流 VT 全链 — 修饰组合键保留/
                    // paste 整块直出）；Linux 默认 ReadKeyPump（ReplShell 内构造）；引用留 Host 收尾 Dispose
                    inputPump: inputPump,
                    // TASK066 板块A — 终端状态协调器（null 降级路径仅测试用 — 宿主恒装配）
                    terminalStatus: terminalStatus,
                    // TASK066 板块B — 键位绑定表（用户 json 合并结果 — 全局键层表驱动数据源，
                    // esc/ctrl+g/ctrl+o/ctrl+l/ctrl+r/ctrl+s/ctrl+d 等非模态全局键走表查询）
                    keybindings: keybindingLoad.Profile,
                    // TASK066 板块C — 备用屏控制器（transcript 模态升级 DEC 1049 真备用屏 +
                    // 四鼠标模式组合 1000/1002/1003/1006；与 tuiOutput 同通道主循环串行）
                    alternateScreen: new AlternateScreenController(tuiOutput.Write),
                    // TASK066 板块C — 选中即复制（Release 后 settled 选区 → 引擎剪贴板服务，
                    // 复用 clipboard 同源注入；选区纯空白跳过在 Core 层守卫）
                     copySelection: text => _ = clipboardService?.SetTextAsync(text),
                     // TASK066 板块D — 空闲返回阈值（基准 CLAUDE_CODE_IDLE_THRESHOLD_MINUTES
                     // 环境变量覆盖，默认 75min — Core 不读 env 由宿主读传入）
                     idleReturnThresholdMinutes: int.TryParse(
                         Environment.GetEnvironmentVariable("CLAUDE_CODE_IDLE_THRESHOLD_MINUTES"), out var idleMin) && idleMin > 0
                         ? idleMin
                         : null,
                     // B类 editorMode 键接线 — vim 编辑模式开关（settings.current.editorMode="vim"
                     // 持久启用 + JCC_TUI_VIM 灰度 OR 语义 — EditorModeResolver 唯一判定源，
                     // 对齐 TS GlobalConfig.editorMode；默认关零行为变化 — main #480 落地，
                     // 取代板块E的纯 env 开关形态）
                      vimMode: vimMode,
                      // statusline 键激活 — 模板 + {model} 实时源（IFastModeService.PrimaryModelId
                      // 闭包 — ReplShell 每帧变量替换投影 FooterState.StatusLine Row2 最优先渲染）
                      statusLineTemplate: statusLineTemplate,
                      statusLineModelProvider: () => session.Services.GetService<IFastModeService>()?.PrimaryModelId,
                      // C类 defaultShell 键 — `!` 命令 shell 选择（settings.current.defaultShell 覆盖平台默认）
                      defaultShell: defaultShell,
                      // C类 respectGitignore 键 — @ 文件补全 gitignore 过滤（settings.current.respectGitignore 默认 true）
                      gitignorePredicate: gitignorePredicate,
                      // TASK066 板块C P3 — 滚轮加速度基础步长（基准 CLAUDE_CODE_SCROLL_SPEED 旋钮 —
                      // readScrollSpeedBase :305-310 clamp (0,20] 默认 1；Core 不读 env 由宿主读传入，
                      // 对齐 idleReturnThresholdMinutes 先例）
                      wheelBaseLines: WheelSpeedTracker.ParseScrollSpeedBase(
                          Environment.GetEnvironmentVariable("CLAUDE_CODE_SCROLL_SPEED")),
                      // TASK066 板块E — 图片文件读取委托（粘贴图片路径 → base64 附件；magic 嗅探
                      // mime，读失败 null 降级该行回退纯文本粘贴）
                      loadImageFromFile: LoadImageFromFile,
                      // TASK066 板块E 收尾 — 剪贴板图片读取委托（chat:imagePaste 键主动读取：
                      // win32 PowerShell Get-Clipboard -Format Image → 临时 PNG → base64 附件；
                      // 非 Windows null 降级 → Core 侧 footer 通知"剪贴板未找到图片"）
                      readClipboardImage: ReadClipboardImage);
                transparencyEngine.Attach(shell);
                Probe("ReplShell 构造完成");
                // TASK077 — 启动诊断行注入（退出备用屏后重输出恢复 scrollback 历史）
                foreach (var line in startupLines)
                {
                    shell.AddStartupLine(line);
                }

                await HistorySeeder.SeedAsync(shell, session.Services, session.SessionId).ConfigureAwait(false);
                session.PermissionBridge.Bind(shell);
                // M8-5 命令交互回调接线（/clear 清 TUI 滚动区+scrollback、/exit /quit /logout 结束主循环）—
                // ReplShell 公开入口已由主智能体收编落地（ClearMessagesExceptBannerAndSpinner/RequestExit）；
                // TASK065 F1 融合 — 退出回调带命令名（RequestExit(reason) 上 "⎿ /{reason} — 退出 jcc" 提示行）
                session.CommandCallbacks.Bind(
                    clearScreen: shell.ClearMessagesExceptBannerAndSpinner,
                    onExitRequested: () => shell.RequestExit("exit"));
                // TASK065 F2/S8 融合 — 交互面重绑：confirm/prompt 从组装期权限对话框初值升级为
                // ReplShell 命令对话框同步桥（语义更准：命令确认非工具权限；/commit /login /resume 等
                // 交互命令解锁 — 回合线程挂起等模态按键，对齐 TuiPermissionBridge 先例）；
                // readPassword 密文模态（TASK065 F2b — 回显 PasswordMask 遮蔽 + Enter 提交原文/Esc null，
                // /login 等密文输入命令解锁）
                session.CommandCallbacks.BindInteractions(
                    confirm: shell.ConfirmCommand,
                    prompt: shell.PromptCommand,
                    readPassword: shell.ReadPasswordCommand,
                    beginSessionPicker: (request, ct) => shell.BeginSessionPickerCoreAsync(
                        request.Entries.Select(e => new SessionPickerItem(e.SessionId, e.Preview, e.TimeAgo)).ToList(),
                        request.Title));
                // earlyInput 回灌 — 引擎组装窗口期捕获的按键回填输入框（基准 earlyInput.ts:164-169
                // consume — 启动期输入不丢失：分派链全量复用，shell 状态此时已就绪）
                foreach (var earlyKey in earlyKeys) {
                    shell.HandleKey(earlyKey);
                }

                shell.Run();
                Probe("shell.Run() 返回");
                shell.WriteExit();
                // DSG042 P2 / TASK077 — 输入泵收尾（通道完成 + 读循环取消；阻塞中的
                // ReadConsoleInput/stdin Read 由 IsBackground 线程随进程退出回收 — Dispose 不等待线程终结防挂）
                if (inputPump is IDisposable d)
                {
                    d.Dispose();
                }
            }
        }

        Console.WriteLine("NativeTui.Host 已退出。");
        return 0;
    }

    /// <summary>加载主题配置名（S9 接线 — 引擎 settings.json 的 theme 键经 IConfigurationService 读取）：
    /// auto 经 ThemeSelection.ResolveAutoName 唯一数据源解析（S11 遗留#3 收编 — 小时判定不再本地重写）；
    /// 未配置/读取失败返回 null（ReplShell 侧 ThemeSelection.Resolve 回退 dark）。</summary>
    /// <summary>图片文件读取（TASK066 板块E — 粘贴图片路径→附件委托宿主实现）：
    /// 字节读入 + magic 嗅探 mime（PNG/JPEG/GIF/WebP 四类，基准 imagePaste.ts:270 扩展名集）；
    /// 尺寸解析不做（DTO 可空 — 引擎侧按需降级）；任何失败 null（Core 层降级回退纯文本粘贴）。</summary>
    /// <param name="path">清理后的图片文件路径。</param>
    /// <returns>附件实例；不可读/非图片 null。</returns>
    private static ImageAttachment? LoadImageFromFile(string path) {
        try {
            if (!File.Exists(path)) {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) {
                return null;
            }

            var mediaType = bytes.Length >= 12 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                ? "image/png"
                : bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
                    ? "image/jpeg"
                    : bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46
                        ? "image/gif"
                        : bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50
                            ? "image/webp"
                            : null;
            if (mediaType is null) {
                return null;
            }

            return ImageAttachment.Of(Convert.ToBase64String(bytes), mediaType, Path.GetFileName(path), path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
            return null;
        }
    }

    /// <summary>读取剪贴板图片（TASK066 板块 E 收尾 — chat:imagePaste 委托宿主实现，
    /// 基准 imagePaste.ts:72-78 win32 命令形态）：PowerShell Get-Clipboard -Format Image →
    /// 临时 PNG 落盘 → 字节读入 base64 附件（mediaType 恒 image/png — Save 指定
    /// ImageFormat::Png 已归一，基准 WSL2 BMP 转换分支 :211-218 不触发；resize 降采样
    /// :221-225 与尺寸解析不做 — DTO dimensions 可空，LoadImageFromFile 同简化先例）；
    /// 非 Windows 返回 null（基准 darwin native NSPasteboard 路径 w2 无对应、linux
    /// xclip/wl-paste 形态首版不实现 — w2 主场景 win32，Unix 降级通知反馈）；
    /// 基准 check+save 两进程（:189-204）合并为单进程等价（save 命令内 if ($img)
    /// 守卫即 check — 无图时不落盘，进程启动一次省 ~1s；15s 超时守卫防大位图挂起）。</summary>
    /// <returns>附件实例（filename 基准默认 'Pasted image' — PromptInput.tsx:1161）；
    /// 剪贴板无图/非 Windows/启动失败/超时 null。</returns>
    private static async Task<ImageAttachment?> ReadClipboardImage() {
        if (!OperatingSystem.IsWindows()) {
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), "claude_cli_latest_screenshot.png");
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"$img = Get-Clipboard -Format Image; if ($img) {{ $img.Save('{path.Replace("'", "''")}', [System.Drawing.Imaging.ImageFormat]::Png) }}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!process.Start()) {
                return null;
            }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            // 无图时 if ($img) 守卫不落盘 — 文件不存在即 check 失败（基准 :189-195 checkImage null 语义）
            if (!File.Exists(path)) {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            return bytes.Length == 0
                ? null
                : ImageAttachment.Of(Convert.ToBase64String(bytes), ImageAttachment.DefaultMediaType, ImageAttachment.DefaultFilename);
        } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
            or IOException or OperationCanceledException) {
            // PowerShell 不存在/超时/读盘失败 — null 降级（ReplShell 侧通知反馈）
            return null;
        } finally {
            // 临时文件清理（基准 :231-232 fire-and-forget deleteFile — 同步 finally 收口，
            // temp 归位清理是进程资源卫生 — ExternalEditorLauncher 同先例；失败静默 OS 兜底）
            try {
                File.Delete(path);
            } catch (IOException) {
            }
        }
    }

    private static async Task<string?> LoadThemeNameAsync(IServiceProvider services) {
        try {
            var config = services.GetService<IConfigurationService>();
            if (config is null) {
                return null;
            }

            var theme = await config.GetAsync("theme").ConfigureAwait(false);
            if (string.IsNullOrEmpty(theme) || theme == "auto") {
                return theme == "auto"
                    ? ThemeSelection.ResolveAutoName(DateTime.Now.Hour)
                    : null;
            }

            return theme;
        } catch (Exception ex) when (ex is InvalidOperationException or IOException) {
            return null;
        }
    }

    /// <summary>加载 vim 编辑模式开关（B类 editorMode 键接线 — theme 先例同模式）：
    /// settings.current.editorMode 经 IConfigurationService 读取 → EditorModeResolver 判定
    ///（settings="vim" 或 JCC_TUI_VIM 灰度 OR 语义）；读取失败 false（默认关，对齐 TS 'normal'）。</summary>
    private static async Task<bool> LoadVimModeAsync(IServiceProvider services) {
        try {
            var config = services.GetService<IConfigurationService>();
            if (config is null) {
                return EditorModeResolver.ResolveVimMode(null, Environment.GetEnvironmentVariable(EditorModeResolver.VimEnvVar));
            }

            var editorMode = await config.GetAsync("editorMode").ConfigureAwait(false);
            return EditorModeResolver.ResolveVimMode(editorMode, Environment.GetEnvironmentVariable(EditorModeResolver.VimEnvVar));
        } catch (Exception ex) when (ex is InvalidOperationException or IOException) {
            return false;
        }
    }

    /// <summary>加载 statusline 模板（statusline 键激活 — theme 先例同模式）：
    /// statusline.enabled=true 且 statusline.format 非空 → 返回模板；否则 null（未启用零行为变化）。</summary>
    private static async Task<string?> LoadStatusLineTemplateAsync(IServiceProvider services) {
        try {
            var config = services.GetService<IConfigurationService>();
            if (config is null) {
                return null;
            }

            var enabled = await config.GetAsync("statusline.enabled").ConfigureAwait(false);
            if (enabled != "true") {
                return null;
            }

            var format = await config.GetAsync("statusline.format").ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(format) ? null : format;
        } catch (Exception ex) when (ex is InvalidOperationException or IOException) {
            return null;
        }
    }

    /// <summary>通用单键读取（theme/vim/statusline/defaultShell 先例同模式）：
    /// 经 IConfigurationService 读取键值；服务缺失/读取失败返回 null。</summary>
    private static async Task<string?> LoadSettingAsync(IServiceProvider services, string key) {
        try {
            return services.GetService<IConfigurationService>() is { } config
                ? await config.GetAsync(key).ConfigureAwait(false)
                : null;
        } catch (Exception ex) when (ex is InvalidOperationException or IOException) {
            return null;
        }
    }

    /// <summary>加载 gitignore 过滤委托（respectGitignore 键激活 — settings.current.respectGitignore 默认 true）：
    /// 从 workspaceRoot/.gitignore 加载 GitignoreMatcher → 返回 IsIgnored 闭包；
    /// respectGitignore=false 或无 .gitignore 文件 → null（仅硬编码忽略目录生效零行为变化）。
    /// 单层 .gitignore（多层父目录链登记差异 — 对齐 SearchService.LoadGitignoreMatchers 的完整链后续批次）。</summary>
    private static async Task<Func<string, bool>?> LoadGitignorePredicateAsync(IServiceProvider services, string? workspaceRoot) {
        if (string.IsNullOrEmpty(workspaceRoot))
            return null;

        try {
            var respectRaw = await LoadSettingAsync(services, "respectGitignore").ConfigureAwait(false);
            var respect = respectRaw != "false"; // 默认 true（null/空/true/其他 → true，仅 "false" → false）
            if (!respect)
                return null;

            var gitignorePath = Path.Combine(workspaceRoot, ".gitignore");
            if (!File.Exists(gitignorePath))
                return null;

            var content = await File.ReadAllTextAsync(gitignorePath).ConfigureAwait(false);
            var matcher = GitignoreMatcher.Parse(content);
            return rel => matcher.IsIgnored(rel);
        } catch (Exception ex) when (ex is InvalidOperationException or IOException) {
            return null;
        }
    }

    /// <summary>解析 issue 引用短形态上下文（DSG043 尾批）— cwd 下 `git remote get-url origin`
    /// → GitHub owner/repo（ParseGitHub 纯函数）；git 缺失/无 origin/非 GitHub/进程失败 = null
    ///（短形态 #NNN 不展开，完整形态 owner/repo#NNN 不受影响）。5s 超时守卫防启动挂起。</summary>
    private static async Task<string?> ResolveIssueOwnerRepoAsync() {
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo {
                FileName = "git",
                Arguments = "remote get-url origin",
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!process.Start()) {
                return null;
            }

            var url = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return GitRemoteResolver.ParseGitHub(url.Trim());
        } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
            or IOException or OperationCanceledException) {
            // git 不存在/无 remote/超时 — null 降级（短形态不展开）
            return null;
        }
    }

    /// <summary>持久化主题配置（S11 遗留#1 — /theme 合法切换后写回引擎 settings.json 的 theme 键，
    /// 下次启动 LoadThemeNameAsync 读回同一值形成闭环）。配置服务缺失/写盘失败静默
    ///（ReplShell 侧 PersistThemeAsync 已有同形态容错，此处兜底委托链路上其他 IO 异常）。</summary>
    private static async Task SaveThemeAsync(IServiceProvider services, string themeName) {
        try {
            var config = services.GetService<IConfigurationService>();
            if (config is null) {
                return;
            }

            await config.SetAsync("theme", themeName).ConfigureAwait(false);
        } catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) {
            // 写盘失败静默降级 — 会话级主题仍生效，下次启动回退原配置
        }
    }

    /// <summary>导出对话写盘（S12 四域接线 — ExportDialog File 目标宿主实现）：
    /// conversation-{yyyyMMdd-HHmmss}.md 写入当前目录（同刻冲突递增 -2/-3 后缀防覆盖 —
    /// 对话框模态串行无并发导出，存在性检查即足够）；返回写入文件名（ReplShell 通知反馈
    /// 含文件名），写盘异常向上传播（ReplShell 侧容错降级错误反馈）。</summary>
    private static async Task<string?> SaveConversationExportAsync(string exportText) {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"conversation-{stamp}.md";
        var suffix = 1;
        while (File.Exists(fileName)) {
            suffix++;
            fileName = $"conversation-{stamp}-{suffix}.md";
        }

        await File.WriteAllTextAsync(fileName, exportText).ConfigureAwait(false);
        return fileName;
    }

    /// <summary>输入泵探针（DSG042 P2 — 真实 stdin 路径实测）：管道喂 VT 字节流
    ///（printf '|' 形态 — 终端即往 stdin 写这些字节，管道字节流与终端形态一致）→
    /// RawStdinPump 读线程全链（UTF-8 增量解码/前缀攒齐/paste 整块/修饰组合）→ 键位全表逐位断言。
    /// stdin 经管道输入（探针通道 — 不经交互模式重定向守卫）；EOF 后读线程退出收敛。</summary>
    private static async Task<int> RunSelftestInputAsync() {
        using var pump = new RawStdinPump();
        var keys = new List<KeyPress>();
        var timeoutAt = Environment.TickCount64 + 5000;
        // 收敛 = 读线程退出（stdin EOF — 半截序列残留 pending 属解析器正确语义不产出，不阻塞收敛）
        while (pump.IsReaderAlive) {
            if (Environment.TickCount64 > timeoutAt) {
                Console.Error.WriteLine($"INPUT SELFTEST FAIL — 收敛超时：已收 {keys.Count} 键，pending {pump.PendingCount} 字符");
                return 1;
            }

            pump.Drain(keys.Add);
            await Task.Delay(10).ConfigureAwait(false);
        }

        pump.Drain(keys.Add);

        var failures = new List<string>();
        AssertInputKey(keys, 0, k => k.Kind == KeyKind.Char && k.Input == "a", "字符键 a", failures);
        AssertInputKey(keys, 1, k => k.Kind == KeyKind.Char && k.Input == "b", "字符键 b", failures);
        AssertInputKey(keys, 2, k => k.Ctrl && k.Input == "u", "Ctrl+U（0x15）", failures);
        AssertInputKey(keys, 3, k => k.Kind == KeyKind.LeftArrow, "方向键 ESC[D → LeftArrow", failures);
        AssertInputKey(keys, 4, k => k is { Kind: KeyKind.LeftArrow, Ctrl: true }, "修饰组合 ESC[1;5D → Ctrl+LeftArrow（ReadKey 通道不可得形态）", failures);
        AssertInputKey(keys, 5, k => k.Kind == KeyKind.Paste && k.Input == "中文粘贴内容", "bracketed paste 整块（UTF-8 多字节）", failures);
        AssertInputKey(keys, 6, k => k.Ctrl && k.Input == "_", "undo 键 0x1F → Ctrl+_", failures);
        AssertInputKey(keys, 7, k => k.Kind == KeyKind.Char && k.Input == "你", "中文字符键（多字节解码）", failures);
        AssertInputKey(keys, 8, k => k.Kind == KeyKind.Return, "回车 CR", failures);
        if (keys.Count != 9) {
            failures.Add($"键总数：期望 9 实际 {keys.Count}（多余键：{string.Join("、", keys.Skip(9).Select(k => $"{k.Kind}:{k.Input}"))}）");
        }

        if (failures.Count > 0) {
            Console.Error.WriteLine("INPUT SELFTEST FAIL：");
            foreach (var failure in failures) {
                Console.Error.WriteLine($"  ✖ {failure}");
            }

            return 1;
        }

        Console.WriteLine("INPUT SELFTEST OK — stdin 原始流 VT 全链（解码/前缀攒齐/paste 整块/修饰组合）全部通过");
        return 0;
    }

    /// <summary>单键断言辅助（越界/不匹配记失败带索引诊断）。</summary>
    private static void AssertInputKey(List<KeyPress> keys, int index, Func<KeyPress, bool> predicate, string label, List<string> failures) {
        if (index >= keys.Count) {
            failures.Add($"{label}：越界（仅收 {keys.Count} 键）");
            return;
        }

        if (!predicate(keys[index])) {
            var k = keys[index];
            failures.Add($"{label}：实际 {k.Kind}:{(k.Input ?? string.Empty)}{(k.Ctrl ? " Ctrl" : string.Empty)}{(k.Meta ? " Meta" : string.Empty)}");
        }
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

    /// <summary>TASK065 F7 — 无参选择器拦截命令的参数提示对齐：/model 无参被下方 CommandPickerEngine
    /// 拦截走选择器模态，静态 ArgumentHint "[model-id|default|info]" 诱导用户走带参路径与拦截行为失配 →
    /// 装配 commands 清单时改写为无参选择器诱导（与拦截声明同点维护；GUI 宿主不拦截 /model，
    /// 其 hint 保持引擎真值不受影响）。record with 纯投影 — 源清单不可变。internal — 装配策略测试面。</summary>
    internal static IReadOnlyList<CommandSuggestion> ApplyPickerHints(IReadOnlyList<CommandSuggestion> commands) =>
        commands.Select(c => c.Name is "model" ? c with { ArgumentHint = "（无参打开选择器）" } : c).ToList();
}
