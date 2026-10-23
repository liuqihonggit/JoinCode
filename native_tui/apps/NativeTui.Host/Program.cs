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

    /// <summary>交互模式 — TTY 检查 → UTF-8 → raw mode → 引擎组装（Console.Out 重定向 stderr 防引擎污染渲染）→ REPL 主循环。</summary>
    private static async Task<int> RunInteractiveAsync(bool useRawStdinPump = false) {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) {
            Console.Error.WriteLine("交互模式需要真实终端（stdin/stdout 被重定向）。请直接在终端运行 NativeTui.Host，或用 --selftest 跑非交互冒烟。");
            return 1;
        }

        Console.OutputEncoding = Encoding.UTF8;
        using var raw = new RawConsoleMode();
        Console.Error.WriteLine("引擎启动中…");
        // S12 OSC8 最小集成 — 会话级探测一次（stderr 诊断旁路 — 对齐 DrainDiagnostics 写 stderr 先例）：
        // 探测结果缓存于日志供链接渲染深链路批次消费（本批仅探测 + 说明，见未决问题）
        Console.Error.WriteLine(HyperlinkEmitter.IsHyperlinkSupported()
            ? "[diag] OSC 8 超链接: 支持（Markdown 链接渲染深链路后续批次接入）"
            : "[diag] OSC 8 超链接: 不支持（终端未声明 — 链接降级纯文本，无需动作）");
        using var logLevelScope = EnvVarScope.Set("JCC_LOG_LEVEL", "Error");
        TestEnvironmentDetector.ForceNonInteractive = true;

        // Console.Out → stderr：引擎内部任何 Console 输出不得污染 TUI 渲染流
        // （TUI 渲染走重定向前捕获的独立 writer，不受 SetOut 影响 — 对齐 BridgeServeCommand 先例）
        var tuiOutput = Console.Out;
        var originalOut = Console.Out;
        Console.SetOut(Console.Error);
        // E1 崩溃兜底 — CursorLifecycle 注释契约兑现：raw mode 运行期内任何未处理异常/信号退出路径，
        // 钩子统一补发硬件光标 Show + bracketed paste Disable（using RawConsoleMode 只恢复 console mode，
        // 覆盖不了 DEC 25/2004 两个终端层状态 — 对齐基准 ink.tsx signal-exit 恢复链）
        using var restoreGuard = new TerminalRestoreGuard(tuiOutput);
        // earlyInput — 引擎组装窗口期按键捕获（基准 earlyInput.ts:29-67 — 启动期输入不丢失，
        // 就绪后回填输入框；非阻塞 KeyAvailable 轮询，无按键零耗时）
        var earlyKeys = EarlyInputBuffer.CaptureAvailable(() => Console.KeyAvailable, Console.ReadKey);
        // 终端查询路由器（TASK067 批4 — 探测链路地基：DA1/DECRPM/kitty flags/DSR/XTVERSION
        // 查询-应答 promise；RawStdinPump 前置分流路由；主动探测接入 TerminalCapabilities 为后续增量）
        using var terminalQuerier = new TerminalQuerier(tuiOutput);
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
                var commands = ApplyPickerHints(SlashCommandSource.Load(session.Services));
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
                // S9/S10 接线 — 主题配置名（引擎 settings.json theme 键 → ThemeSelection.Resolve，
                // auto 按本地小时解析 light/dark，对齐引擎 ThemeCommand.ApplyTheme）+ 命令使用记忆
                //（~/.jcc/command-history.json 坏文件静默降级空记忆）+ 工作区根（@ 文件补全索引根，banner cwd 同源）
                var themeName = await LoadThemeNameAsync(session.Services).ConfigureAwait(false);
                var commandUsage = await CommandUsageMemory.LoadAsync(CommandUsageMemory.DefaultFilePath).ConfigureAwait(false);
                var rawInputPump = useRawStdinPump ? new RawStdinPump(terminalQuerier) : null;
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
                foreach (var keybindingError in keybindingLoad.Errors)
                {
                    Console.Error.WriteLine(keybindingError.ToString());
                }
                var shell = new ReplShell(pickerEngine, banner, tuiOutput, commands: commands,
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
                    // DSG042 P2 — 输入泵通道：默认 ReadKeyPump（现役稳定）；
                    // --raw-input 灰度启用 RawStdinPump（stdin 原始流 VT 全链 — 修饰组合键保留/
                    // paste 整块直出；双终端实测矩阵跑齐后 P3 切默认）；引用留 Host 收尾 Dispose
                    inputPump: rawInputPump,
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
                    copySelection: text => _ = clipboardService?.SetTextAsync(text));
                pickerEngine.Attach(shell);
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
                    readPassword: shell.ReadPasswordCommand);
                // earlyInput 回灌 — 引擎组装窗口期捕获的按键回填输入框（基准 earlyInput.ts:164-169
                // consume — 启动期输入不丢失：分派链全量复用，shell 状态此时已就绪）
                foreach (var earlyKey in earlyKeys) {
                    shell.HandleKey(earlyKey);
                }

                shell.Run();
                shell.WriteExit();
                // DSG042 P2 — 原始流泵收尾（通道完成 + 读循环取消；阻塞中的 stdin Read 由
                // IsBackground 线程随进程退出回收 — Dispose 不等待线程终结防挂）
                rawInputPump?.Dispose();
            }
        } finally {
            Console.SetOut(originalOut);
        }

        Console.WriteLine("NativeTui.Host 已退出。");
        return 0;
    }

    /// <summary>加载主题配置名（S9 接线 — 引擎 settings.json 的 theme 键经 IConfigurationService 读取）：
    /// auto 经 ThemeSelection.ResolveAutoName 唯一数据源解析（S11 遗留#3 收编 — 小时判定不再本地重写）；
    /// 未配置/读取失败返回 null（ReplShell 侧 ThemeSelection.Resolve 回退 dark）。</summary>
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
