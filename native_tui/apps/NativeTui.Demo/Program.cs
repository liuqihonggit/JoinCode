namespace NativeTui.Demo;

/// <summary>
/// M6a 手动验收 demo（ADR 0080）— REPL 全链路真实运行（非 mock）：
/// raw mode + 50ms 动画 tick + 帧双缓冲（共享 StylePool）+ CellDiffer 差分 + DEC 2026 原子上屏
/// + InputBuffer 编辑 + 模拟回合（Thinking → ToolUse → Responding）。
/// 交互：Enter 提交演示命令（md / diff / perm / all — 未知命令回 Error 诱导）；Esc 忙时打断/闲时退出；Ctrl+C 退出；↑↓ 滚动消息区。
/// --selftest：非交互冒烟（spinner 三帧链路 + 静态零输出 + 输入编辑），退出码 0/1。
/// </summary>
public static class Program
{
    /// <summary>
    /// 入口 — 全局选项扫描（--model &lt;name&gt; / --viewport）后按剩余位置参数分派：
    /// 自检/bridge 自回环/bridge 交互/交互模式（默认 inline 渲染，--viewport 走旧全屏 diff 模式）。
    /// </summary>
    public static int Main(string[] args)
    {
        string? model = null;
        var viewport = false;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--model" && i + 1 < args.Length)
            {
                model = args[++i];
            }
            else if (args[i] == "--viewport")
            {
                viewport = true;
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        return rest.ToArray() is ["--selftest"] ? RunSelftest()
            : rest is ["--bridge-selftest"] ? RunBridgeSelftest()
            : rest is ["--bridge-echo-child"] ? RunBridgeEchoChild()
            : rest is ["--capture", string capturePath] ? RunCapture(capturePath)
            : rest is ["--bridge", string exePath] ? RunBridgeInteractive(exePath, model, viewport)
            : RunInteractive(model, viewport);
    }

    /// <summary>
    /// 采集模式（L2 截图校准的 C# 侧采集端）— 渲染 80×24 固定基准场景（ReferenceScene）
    /// 并把整帧 ANSI 写入指定文件，供 tool/screenshot/compare/compare_reference.py 与 TS 参考帧做像素比对。
    /// </summary>
    private static int RunCapture(string capturePath)
    {
        File.WriteAllText(capturePath, ReferenceScene.ComposeAnsi());
        Console.WriteLine($"CAPTURE OK — 80×24 基准场景已写入 {capturePath}（clawd 9×3 + prompt 边框 region 可比对）");
        return 0;
    }

    /// <summary>交互模式：进 raw mode 跑共享 REPL 外壳（ReplShell + SimulatedEngine 演示命令路由 md/diff/perm/all；inline 模式退出只清尾部动态区，历史留终端 scrollback）。
    /// TASK056：注入演示命令清单启用斜杠补全（输入 / 弹建议列表）+ 历史环默认启用（↑↓ 翻阅提交记录）。</summary>
    private static int RunInteractive(string? model = null, bool viewport = false)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("交互模式需要真实终端（stdin/stdout 被重定向）。请直接在终端运行 NativeTui.Demo.exe，或用 --selftest 跑非交互冒烟。");
            return 1;
        }

        Console.OutputEncoding = Encoding.UTF8;
        using var raw = new RawConsoleMode();
        var version = typeof(Program).Assembly.GetName().Version is { } v ? v.ToString(3) : "0.0.0";
        var info = SessionInfo.Create(version, model);
        var banner = new BannerItem(info.Version, info.Model, info.Billing, info.WorkingDirectory);
        // M7-6 模型选择器演示 — 装饰模拟引擎拦截 /model 打开 Picker（模拟清单，真实引擎见 NativeTui.Host）
        // TASK058 供应商选择器演示 — 外层装饰拦截 /vendor（模拟清单）
        var pickerEngine = new CommandPickerEngine(
            new CommandPickerEngine(new SimulatedEngine(), "/model", "模型", DemoModelItems, _ => { }),
            "/vendor",
            "供应商",
            DemoVendorItems,
            _ => { });
        var shell = new ReplShell(pickerEngine, banner, Console.Out, inline: !viewport, seedMessages: SeedMessages(), commands: DemoCommands(), codeHighlighter: new TextMateCodeHighlighter(dark: true));
        pickerEngine.Attach(shell);
        shell.Run();
        shell.WriteExit();
        Console.WriteLine("NativeTui.Demo 已退出。");
        return 0;
    }

    /// <summary>Demo 演示命令清单（斜杠补全数据源）— 与 SimulatedEngine.ParseCommand 路由同源 + /model、/vendor（CommandPickerEngine 装饰器拦截）。</summary>
    private static IReadOnlyList<CommandSuggestion> DemoCommands() =>
    [
        new CommandSuggestion("md", "Markdown 全形态渲染", [], IsHidden: false),
        new CommandSuggestion("diff", "结构化补丁", [], IsHidden: false),
        new CommandSuggestion("perm", "权限对话框", [], IsHidden: false),
        new CommandSuggestion("all", "完整回合（补丁 + Markdown）", [], IsHidden: false),
        new CommandSuggestion("agents", "并行子代理分组（可带数量 agents 300）", [], IsHidden: false),
        new CommandSuggestion("model", "切换模型（M7-6 选择器）", [], IsHidden: false),
        new CommandSuggestion("vendor", "切换供应商（TASK058 选择器）", [], IsHidden: false),
    ];

    /// <summary>Demo 模拟模型清单（M7-6 选择器演示 — 真实引擎从 IModelCatalog 后端获取，见 NativeTui.Host ModelPickerAssembly）。</summary>
    private static IReadOnlyList<ModelPickerItem> DemoModelItems() =>
    [
        new ModelPickerItem("gpt-5.3", "GPT 5.3", "最强推理 · $3/$0.22 per token", IsCurrent: true),
        new ModelPickerItem("gpt-5.3-mini", "GPT 5.3 Mini", "日常任务最佳 · $0.4/$0.03 per token", IsCurrent: false),
        new ModelPickerItem("gpt-nano", "GPT Nano", "最快响应 · 轻量便宜", IsCurrent: false),
    ];

    /// <summary>Demo 模拟供应商清单（TASK058 选择器演示 — 真实引擎从 VendorKind 枚举投影，见 NativeTui.Host VendorPickerAssembly）。</summary>
    private static IReadOnlyList<ModelPickerItem> DemoVendorItems() =>
    [
        new ModelPickerItem("zhipu", "智谱 AI", "GLM 系列 · 默认 glm-4.7", IsCurrent: true),
        new ModelPickerItem("deepseek", "DeepSeek", "深度求索 · 默认 deepseek-chat", IsCurrent: false),
        new ModelPickerItem("openai", "OpenAI", "GPT 系列 · 默认 gpt-5.3", IsCurrent: false),
        new ModelPickerItem("anthropic", "Anthropic", "Claude 系列 · 默认 claude-sonnet-4", IsCurrent: false),
    ];

    /// <summary>模拟模式种子消息（示例对话 — banner 之后、spinner 之前插入滚动区；assistant 回复为 Markdown 行内样式演示，M7-4 启动即见渲染；末条为演示命令提示）。</summary>
    private static IReadOnlyList<IScrollItem> SeedMessages() =>
    [
        new UserMessageItem("这个 TUI 是像素级对齐 Claude Code 的自研实现吗？"),
        new MarkdownMessageItem(new MarkdownDocument(
        [
            new MarkdownParagraph(
            [
                new MarkdownSegment("是的 — 纯 C# 自研渲染管线：", MarkdownStyle.None),
                new MarkdownSegment("Screen cell 网格", MarkdownStyle.Bold),
                new MarkdownSegment(" + ", MarkdownStyle.None),
                new MarkdownSegment("CellDiffer", MarkdownStyle.Code),
                new MarkdownSegment(" 差分 + ", MarkdownStyle.None),
                new MarkdownSegment("DEC 2026", MarkdownStyle.Code),
                new MarkdownSegment(" 原子上屏。", MarkdownStyle.None),
            ]),
        ])),
        new ToolCallItem("Read", "native_tui/src/NativeTui.Core/repl/ReplView.cs"),
        new ToolResultItem("41 行 — REPL 整屏视图（footer + 输入框 + 消息滚动区）"),
        new AssistantMessageItem("演示命令：md（Markdown 全形态）/ diff（结构化补丁）/ perm（权限对话框）/ all（完整回合）/ agents（并行子代理分组，300 规模可带数量）— 输入命令回车触发；其他输入回 Error 诱导。"),
    ];

    /// <summary>
    /// bridge 交互模式（ADR 0141 / ADR 0080 真实进程验收）— 拉起引擎子进程（stdio 管道），
    /// 主循环内排空事件 → ReplBridgeDriver 应用到视图；提交/打断写子进程 stdin。
    /// 退出（Ctrl+C/闲时 Esc）：Dispose 发 quit → 引擎退出 → inline 清尾部动态区（历史留 scrollback）。
    /// </summary>
    private static int RunBridgeInteractive(string exePath, string? model = null, bool viewport = false)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("bridge 交互模式需要真实终端（stdin/stdout 被重定向）。请直接在终端运行，引擎 stderr 会直通显示。");
            return 1;
        }

        BridgeClient client;
        try
        {
            client = BridgeClient.Start(exePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or FileNotFoundException)
        {
            Console.Error.WriteLine($"引擎启动失败：{ex.Message}。请核对 --bridge <exe完整路径> 参数（本地联调可用本程序自身：NativeTui.Demo.exe --bridge NativeTui.Demo.exe 不支持，请改用 --bridge-selftest 验证协议）。");
            return 1;
        }

        using (var engine = new BridgeEngine(client))
        {
            Console.OutputEncoding = Encoding.UTF8;
            using var raw = new RawConsoleMode();
            var version = typeof(Program).Assembly.GetName().Version is { } v ? v.ToString(3) : "0.0.0";
            var info = SessionInfo.Create(version, model);
            var banner = new BannerItem(info.Version, info.Model, info.Billing, info.WorkingDirectory);
            // M8-8 后续 — x 停止后台任务：bridge 模式经 IReplEngine 接口写子进程 stdin stop_task 请求
            //（引擎侧分派 CancelFork/StopTeammate 后 cancelled 终态事件泵回推面板）
            var shell = new ReplShell(engine, banner, Console.Out, inline: !viewport,
                onStopTask: engine.StopBackgroundTask,
                codeHighlighter: new TextMateCodeHighlighter(dark: true));
            shell.Run();
            shell.WriteExit();
        }

        Console.WriteLine("NativeTui.Demo bridge 会话已退出。");
        return 0;
    }

    /// <summary>
    /// bridge 自回环验证（真进程级，ADR 0080）— spawn 自身 echo-child 作为引擎模拟，
    /// 走完整 stdin/stdout 管道 + 协议 roundtrip：submit → 事件序列 → 退出码 0/1。
    /// </summary>
    private static int RunBridgeSelftest()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null)
        {
            Console.Error.WriteLine("FAIL: 无法定位自身 exe 路径");
            return 1;
        }

        var psi = new ProcessStartInfo(exePath, "--bridge-echo-child")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine("FAIL: 子进程启动失败");
            return 1;
        }

        BridgeProtocol.WriteRequest(process.StandardInput, new BridgeRequest.Submit("bridge roundtrip"));
        BridgeProtocol.WriteRequest(process.StandardInput, new BridgeRequest.SetPermissionMode("plan"));
        process.StandardInput.Close();

        var received = new List<BridgeEvent>();
        var failures = new List<string>();
        while (BridgeProtocol.TryReadEvent(process.StandardOutput, out var bridgeEvent, out var diagnostic) == BridgeReadResult.Event)
        {
            received.Add(bridgeEvent!);
            if (diagnostic is not null)
            {
                failures.Add("诊断泄漏: " + diagnostic);
            }
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            failures.Add($"echo-child 退出码 {process.ExitCode}");
        }

        AssertEvent(received, e => e is BridgeEvent.SessionInfo { Version.Length: > 0, PermissionMode.Length: > 0, Cwd.Length: > 0 }, "session_info 握手（版本/权限模式/cwd）", failures);
        AssertEvent(received, e => e is BridgeEvent.PermissionModeChanged { Mode: "plan" }, "permission_mode_changed 回执 plan", failures);
        ExpectEvent<BridgeEvent.TurnStarted>(received, failures);
        ExpectEvent<BridgeEvent.ThinkingStarted>(received, failures);
        AssertEvent(received, e => e is BridgeEvent.ThinkingEnded { DurationMs: > 0 }, "thinking_ended durationMs>0", failures);
        ExpectEvent<BridgeEvent.ToolCall>(received, failures);
        ExpectEvent<BridgeEvent.ToolResult>(received, failures);
        AssertEvent(received, e => e is BridgeEvent.AssistantMessage { Text.Length: > 0 } am && am.Text.Contains("bridge roundtrip"), "assistant_message 回显提交文本", failures);
        ExpectEvent<BridgeEvent.TurnCompleted>(received, failures);

        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.Error.WriteLine("FAIL: " + failure);
            }

            return 1;
        }

        Console.WriteLine("BRIDGE SELFTEST OK — 跨进程 stdin/stdout 管道 + 行分格 JSON 协议 roundtrip 全部通过");
        return 0;
    }

    /// <summary>断言事件序列包含指定类型。</summary>
    private static void ExpectEvent<T>(List<BridgeEvent> received, List<string> failures)
        where T : BridgeEvent
    {
        if (received.All(e => e is not T))
        {
            failures.Add($"缺少事件 {typeof(T).Name}（收到：{string.Join(", ", received.Select(e => e.GetType().Name))}）");
        }
    }

    /// <summary>断言事件序列存在满足谓词的事件。</summary>
    private static void AssertEvent(List<BridgeEvent> received, Func<BridgeEvent, bool> predicate, string what, List<string> failures)
    {
        if (!received.Any(predicate))
        {
            failures.Add($"断言失败: {what}");
        }
    }

    /// <summary>
    /// echo-child — 分派式引擎模拟：读 stdin 请求（TryReadRequest），
    /// 启动即发 session_info 握手（模拟引擎行为）；submit → 回放完整回合事件序列
    /// （assistant_message 回显提交文本）；set_permission_mode → permission_mode_changed 回执；
    /// interrupt → turn_interrupted；quit/Eof → 退出。协议统一 LF（显式 Write('\n')）。
    /// </summary>
    private static int RunBridgeEchoChild()
    {
        var writer = Console.Out;
        WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.SessionInfo("1.0.0", "echo-model", "auto", BypassAvailable: true, Environment.CurrentDirectory)));
        writer.Flush();
        var exit = false;
        while (!exit)
        {
            var result = BridgeProtocol.TryReadRequest(Console.In, out var request, out var diagnostic);
            switch (result)
            {
                case BridgeReadResult.Eof:
                    exit = true;
                    break;
                case BridgeReadResult.Skipped:
                    Console.Error.WriteLine(diagnostic);
                    break;
                default:
                    HandleEchoRequest(request!, writer);
                    exit = request is BridgeRequest.Quit;
                    break;
            }
        }

        return 0;
    }

    /// <summary>分派单条请求：submit → 回放完整回合事件序列（assistant_message 回显提交文本）；set_permission_mode → 回执；interrupt → turn_interrupted。</summary>
    private static void HandleEchoRequest(BridgeRequest request, TextWriter writer)
    {
        switch (request)
        {
            case BridgeRequest.Submit(var text):
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.TurnStarted()));
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.ThinkingStarted()));
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.ThinkingEnded(5000)));
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.ToolCall("Read", "bridge/echo")));
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.ToolResult("6 events")));
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.AssistantMessage($"echo: {text} — 协议链路正常。")));
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.TurnCompleted()));
                writer.Flush();
                break;
            case BridgeRequest.SetPermissionMode(var mode):
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.PermissionModeChanged(mode)));
                writer.Flush();
                break;
            case BridgeRequest.Interrupt:
                WriteLine(writer, BridgeProtocol.SerializeEvent(new BridgeEvent.TurnInterrupted()));
                writer.Flush();
                break;
        }
    }

    /// <summary>写一行 + 显式 LF + Flush（协议统一 \n）。</summary>
    private static void WriteLine(TextWriter writer, string line)
    {
        writer.Write(line);
        writer.Write('\n');
    }

    /// <summary>自检模式：五组冒烟断言（无终端依赖，CI 可跑）。</summary>
    private static int RunSelftest()
    {
        var failures = new List<string>();
        SelftestSpinnerChain(failures);
        SelftestStaticZeroOutput(failures);
        SelftestInputBuffer(failures);
        SelftestKeyPressParser(failures);
        SelftestInlineChain(failures);
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.Error.WriteLine("FAIL: " + failure);
            }

            return 1;
        }

        Console.WriteLine("SELFTEST OK — spinner 动画链路 / 静态零输出 / 输入编辑 / inline 渲染链路 全部通过");
        return 0;
    }

    /// <summary>inline 渲染链路：首帧逐行写（不清屏）+ 增长 append + 零变化零输出。</summary>
    private static void SelftestInlineChain(List<string> failures)
    {
        var pool = new StylePool();
        using var screenA = new Screen(cols: 60, rows: 6, sharedStyles: pool);
        using var screenB = new Screen(cols: 60, rows: 8, sharedStyles: pool);
        var view = new ReplView();
        view.Messages.Add(new AssistantMessageItem("inline frame"));
        var composer = new InlineComposer();

        view.Render(screenA, Theme.Dark);
        var first = composer.Compose(prev: null, screenA, viewportRows: 24, prevCursor: new(0, 0), frameEndCursor: view.InputCursorPos(screenA.Rows, screenA.Cols, 0), out var cursor);
        if (first.Length == 0 || first.Contains(Csi.EraseScreen()) || !first.Contains("inline frame"))
        {
            failures.Add("inline 首帧: 期望逐行写且不清屏且含消息文本");
        }

        var view2 = new ReplView();
        view2.Messages.Add(new AssistantMessageItem("inline frame"));
        view2.Messages.Add(new AssistantMessageItem("appended row"));
        view2.Render(screenB, Theme.Dark);
        var grown = composer.Compose(screenA, screenB, viewportRows: 24, cursor, view2.InputCursorPos(screenB.Rows, screenB.Cols, 0), out _);
        if (grown.Length == 0 || grown.Contains(Csi.EraseScreen()) || !grown.Contains("appended row"))
        {
            failures.Add("inline 增长: 期望 append 新行且不清屏");
        }

        var steady = view2.InputCursorPos(screenB.Rows, screenB.Cols, 0);
        if (composer.Compose(screenB, screenB, viewportRows: 24, steady, steady, out _) is { Length: > 0 })
        {
            failures.Add("inline 零变化: 期望零输出");
        }
    }

    /// <summary>spinner 动画链路：首帧全量（EraseScreen + 消息可见），后续帧 glyph 变化非空。</summary>
    private static void SelftestSpinnerChain(List<string> failures)
    {
        var pool = new StylePool();
        using var screenA = new Screen(cols: 80, rows: 24, sharedStyles: pool);
        using var screenB = new Screen(cols: 80, rows: 24, sharedStyles: pool);
        var view = new ReplView();
        var spinner = new SpinnerItem { Message = "Thinking…", Mode = SpinnerMode.Thinking };
        view.Messages.Add(spinner);
        var composer = new FrameComposer();

        Screen? prev = null;
        for (var tick = 0; tick < 3; tick++)
        {
            var back = tick % 2 == 0 ? screenA : screenB;
            back.Clear();
            spinner.AnimTimeMs = tick * 120L;
            spinner.WallNowMs = 1000 + tick * 120L;
            spinner.LoadingStartMs = 1000;
            view.Render(back, Theme.Dark);
            var output = composer.Compose(prev, back);
            if (output.Length == 0)
            {
                failures.Add($"spinner 链路 tick {tick}: 帧输出为空");
            }

            if (tick == 0 && !output.Contains(Csi.EraseScreen()))
            {
                failures.Add("spinner 链路首帧缺少 EraseScreen");
            }

            if (tick == 0 && !output.Contains("Thinking"))
            {
                failures.Add("spinner 链路首帧缺少消息文本");
            }

            prev = back;
        }
    }

    /// <summary>静态零输出：同内容双帧 diff 必须为空串（FrameComposer 无变化零输出）。</summary>
    private static void SelftestStaticZeroOutput(List<string> failures)
    {
        var pool = new StylePool();
        using var screenA = new Screen(cols: 60, rows: 10, sharedStyles: pool);
        using var screenB = new Screen(cols: 60, rows: 10, sharedStyles: pool);
        var view = new ReplView();
        view.Messages.Add(new AssistantMessageItem("static frame"));
        view.Input = "hello";
        view.CursorCol = 5;
        RenderStaticFrame(screenA, view);
        RenderStaticFrame(screenB, view);
        if (new FrameComposer().Compose(screenA, screenB).Length != 0)
        {
            failures.Add("静态零输出: 相同内容双帧 diff 应为空串");
        }
    }

    private static void RenderStaticFrame(Screen screen, ReplView view)
    {
        screen.Clear();
        view.Render(screen, Theme.Dark);
    }

    /// <summary>输入编辑冒烟：插入/移动/退格路径。</summary>
    private static void SelftestInputBuffer(List<string> failures)
    {
        var buffer = new InputBuffer();
        buffer.Insert('h');
        buffer.Insert('i');
        buffer.Left();
        buffer.Insert('!');
        buffer.Backspace();
        if (buffer.Text != "hi" || buffer.CursorCol != 1)
        {
            failures.Add($"输入编辑: 期望 hi@1 实际 {buffer.Text}@{buffer.CursorCol}");
        }
    }

    /// <summary>按键解码冒烟：方向键/Ctrl/Meta 序列 + kill ring 编辑链。</summary>
    private static void SelftestKeyPressParser(List<string> failures)
    {
        var span = "\u001b[D".AsSpan();
        if (!KeyPressParser.TryParseNext(ref span, out var left) || left.Kind != KeyKind.LeftArrow)
        {
            failures.Add($"按键解码: ESC[D 期望 LeftArrow 实际 {left?.Kind}");
        }

        var ctrlSpan = "\u0015".AsSpan();
        if (!KeyPressParser.TryParseNext(ref ctrlSpan, out var ctrlU) || !ctrlU.Ctrl || ctrlU.Input != "u")
        {
            failures.Add($"按键解码: Ctrl+U 期望 Ctrl/u 实际 {ctrlU?.Ctrl}/{ctrlU?.Input}");
        }

        KillRing.Clear();
        var buffer = InputBuffer.Of("hello world", cursorCol: 6);
        buffer.KillWordBefore();
        buffer.Yank();
        if (buffer.Text != "hello world" || buffer.CursorCol != 6)
        {
            failures.Add($"kill/yank: 期望 hello world@6 实际 {buffer.Text}@{buffer.CursorCol}");
        }
    }
}

/// <summary>
/// 会话信息 — banner 真实数据源：版本/模型/计费/cwd。bridge 模式收到引擎 session_info
/// 事件后由 ReplBridgeDriver 原地更新 BannerItem；模型未设置为"当前模型未设置"。
/// </summary>
/// <param name="Version">版本（seed=TUI 程序集版本；bridge=引擎 session_info 版本）。</param>
/// <param name="Model">模型名（--model 参数或"当前模型未设置"）。</param>
/// <param name="Billing">计费方式（模拟配置，无真实登录态可查）。</param>
/// <param name="WorkingDirectory">工作目录（进程 cwd 快照；bridge 模式由 session_info 覆盖）。</param>
internal sealed record SessionInfo(string Version, string Model, string Billing, string WorkingDirectory)
{
    /// <summary>引擎未设置模型时的 banner 显示文案。</summary>
    public const string NoModel = "当前模型未设置";

    /// <summary>默认会话信息（模型未设置）。</summary>
    public static SessionInfo Create(string version, string? model = null) =>
        new(version, model ?? NoModel, "API 按量计费", Environment.CurrentDirectory);
}
