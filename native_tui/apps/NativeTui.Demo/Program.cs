namespace NativeTui.Demo;

/// <summary>
/// M6a 手动验收 demo（ADR 0080）— REPL 全链路真实运行（非 mock）：
/// raw mode + 50ms 动画 tick + 帧双缓冲（共享 StylePool）+ CellDiffer 差分 + DEC 2026 原子上屏
/// + InputBuffer 编辑 + 模拟回合（Thinking → ToolUse → Responding）。
/// 交互：Enter 提交（触发模拟回合）；Esc 忙时打断/闲时退出；Ctrl+C 退出；↑↓ 滚动消息区。
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
            : rest is ["--bridge", string exePath] ? RunBridgeInteractive(exePath, model, viewport)
            : RunInteractive(model, viewport);
    }

    /// <summary>交互模式：进 raw mode 跑主循环（inline 模式退出只清尾部动态区，历史留终端 scrollback）。</summary>
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
        var session = new DemoSession(session: SessionInfo.Create(version, model), inline: !viewport);
        session.Run();
        session.WriteExit();
        Console.WriteLine("NativeTui.Demo 已退出。");
        return 0;
    }

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

        using (client)
        {
            Console.OutputEncoding = Encoding.UTF8;
            using var raw = new RawConsoleMode();
            var version = typeof(Program).Assembly.GetName().Version is { } v ? v.ToString(3) : "0.0.0";
            var session = new DemoSession(client, SessionInfo.Create(version, model), inline: !viewport);
            session.Run();
            session.WriteExit();
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

/// <summary>
/// demo 会话 — 持有全部运行时状态（视图/输入/双缓冲/回合状态机），
/// 主循环每 50ms 一拍：排空键盘 → 推进回合（模拟状态机或 bridge 事件排空）→ 渲染差分上屏 → 帧率对齐。
/// bridge 模式（传入 BridgeClient）：回合生命周期由引擎事件驱动（ReplBridgeDriver 应用），
/// 提交/打断写子进程 stdin；引擎退出时追加错误消息并结束会话。
/// </summary>
internal sealed class DemoSession
{
    /// <summary>动画 tick 周期（基准 spinner setInterval 50ms）。</summary>
    private const int TickMs = 50;

    /// <summary>模拟回合的阶段。</summary>
    private enum TurnPhase
    {
        /// <summary>空闲（无回合）。</summary>
        Idle,

        /// <summary>思考中（0-1.2s）。</summary>
        Thinking,

        /// <summary>工具执行中（1.2-2.4s）。</summary>
        ToolUse,

        /// <summary>响应输出中（2.4-5.4s）。</summary>
        Responding,
    }

    private readonly Theme _theme = Theme.Dark;
    private readonly ReplView _view = new();
    private readonly InputBuffer _input = new();
    private readonly SpinnerItem _spinner = new() { Message = string.Empty };
    private readonly FrameComposer _composer = new();
    private readonly StylePool _pool = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>bridge 引擎客户端（null = 模拟回合模式）。</summary>
    private readonly BridgeClient? _client;

    /// <summary>bridge 事件应用器（bridge 模式构造）。</summary>
    private readonly ReplBridgeDriver? _driver;

    /// <summary>启动 banner（消息首项 — bridge 模式由 session_info 事件原地更新真实数据）。</summary>
    private readonly BannerItem _banner;

    private Screen _front = null!;
    private Screen _back = null!;
    private bool _frontValid;
    private long _animTimeMs;
    private TurnPhase _phase = TurnPhase.Idle;
    private long _turnStartMs;
    private bool _running = true;

    /// <summary>创建会话（传 client 进入 bridge 模式：回合由引擎事件驱动；inline 走 log-update 主屏渲染）。</summary>
    public DemoSession(BridgeClient? client = null, SessionInfo? session = null, bool inline = true)
    {
        _client = client;
        _inline = inline;
        _session = session ?? SessionInfo.Create("0.0.0");
        _banner = new BannerItem(_session.Version, _session.Model, _session.Billing, _session.WorkingDirectory);
        _driver = client is null ? null : new ReplBridgeDriver(_view, _spinner, _banner);
    }

    /// <summary>会话信息（banner 真实数据源）。</summary>
    private readonly SessionInfo _session;

    /// <summary>inline（主屏 log-update）渲染模式 — 默认；false = 视口全屏 diff（--viewport 旧模式）。</summary>
    private readonly bool _inline;

    /// <summary>inline 帧组装器。</summary>
    private readonly InlineComposer _inlineComposer = new();

    /// <summary>inline 模式上一帧尾光标（内容坐标）。</summary>
    private CursorPos _cursor;

    /// <summary>inline 模式尺寸重建后的旧帧（跨尺寸 diff：增长走 append、宽度变走 fullReset）。</summary>
    private Screen? _prevOverride;

    /// <summary>主循环 — 每拍：排空键盘 → 推进回合（模拟或 bridge）→ 渲染差分上屏 → 对齐 50ms。</summary>
    public void Run()
    {
        SeedContent();
        while (_running)
        {
            var tickStart = _clock.ElapsedMilliseconds;
            DrainInput();
            if (_client is null)
            {
                AdvanceTurn();
            }
            else
            {
                DrainBridge();
            }

            RenderAndFlush();
            PaceTick(tickStart);
        }
    }

    /// <summary>初始内容 — 启动 banner（两种模式共有，对齐基准 LogoHeader 挂消息列表头部）+ 模拟模式 seed 几条示例消息；bridge 模式滚动区留空（spinner 常驻尾部），banner 真实数据待 session_info 事件。</summary>
    private void SeedContent()
    {
        _view.Footer = new FooterState
        {
            Mode = PermissionMode.Auto,
            AgentsHint = "← agents",
            LoginError = "未登录 · 运行 /login",
            Notification = $"{Figures.EffortMedium} 中等 · /effort",
        };
        _view.Messages.Add(_banner);
        if (_client is null)
        {
            _view.Messages.Add(new UserMessageItem("这个 TUI 是像素级对齐 Claude Code 的自研实现吗？"));
            _view.Messages.Add(new AssistantMessageItem("是的 — 纯 C# 自研渲染管线：Screen cell 网格 + CellDiffer 差分 + DEC 2026 原子上屏。"));
            _view.Messages.Add(new ToolCallItem("Read", "native_tui/src/NativeTui.Core/repl/ReplView.cs"));
            _view.Messages.Add(new ToolResultItem("41 行 — REPL 整屏视图（footer + 输入框 + 消息滚动区）"));
        }

        _view.Messages.Add(_spinner);
    }

    /// <summary>排空 bridge 事件泵：事件应用视图 + 诊断记 stderr + 引擎退出检测。</summary>
    private void DrainBridge()
    {
        foreach (var bridgeEvent in _client!.DrainEvents())
        {
            _driver!.Apply(bridgeEvent);
        }

        foreach (var diagnostic in _client.DrainDiagnostics())
        {
            Console.Error.WriteLine(diagnostic);
        }

        if (!_client.IsEngineAlive && !_driver!.IsBusy)
        {
            _view.Messages.Insert(_view.Messages.Count - 1, new ErrorMessageItem("引擎进程已退出，会话结束。"));
            _running = false;
        }
    }

    /// <summary>新消息插入到常驻 spinner 之前（spinner 恒为滚动区最后一项）。</summary>
    private void AddMessage(IScrollItem item) =>
        _view.Messages.Insert(_view.Messages.Count - 1, item);

    /// <summary>排空键盘事件（非阻塞）— ReadKey 归一 KeyPress 后统一分派。</summary>
    private void DrainInput()
    {
        while (_running && Console.KeyAvailable)
        {
            HandleKey(Console.ReadKey(intercept: true));
        }
    }

    /// <summary>按键归一 + 分派（对齐基准 useTextInput.mapKey 优先级顺序）。</summary>
    private void HandleKey(ConsoleKeyInfo keyInfo)
    {
        var key = ConsoleKeyAdapter.ToKeyPress(keyInfo);
        if (key is null)
        {
            return;
        }

        HandleKeyPress(key);
    }

    /// <summary>按键分派 — Emacs 编辑（Ctrl/Meta 词操作 + kill ring）优先，控制键走编辑/滚动，字符插入。</summary>
    private void HandleKeyPress(KeyPress key)
    {
        if (key.Kind == KeyKind.Escape)
        {
            HandleEscape();
            return;
        }

        if (key.Ctrl && key.Kind is KeyKind.LeftArrow or KeyKind.RightArrow)
        {
            MoveWord(key.Kind == KeyKind.LeftArrow ? -1 : 1);
            return;
        }

        if (key.Kind == KeyKind.Backspace)
        {
            if (key.Meta || key.Ctrl)
            {
                _input.KillWordBefore();
            }
            else
            {
                _input.Backspace();
            }

            return;
        }

        if (key.Kind == KeyKind.Delete)
        {
            if (key.Meta)
            {
                _input.KillToLineEnd();
            }
            else
            {
                _input.Delete();
            }

            return;
        }

        if (key.Ctrl && key.Kind == KeyKind.Char)
        {
            HandleCtrlKey(key.Input);
            return;
        }

        if (key.Meta && key.Kind == KeyKind.Char)
        {
            HandleMetaKey(key.Input);
            return;
        }

        switch (key.Kind)
        {
            case KeyKind.Return:
                Submit();
                break;
            case KeyKind.BackTab:
                CyclePermissionMode();
                break;
            case KeyKind.LeftArrow:
                _input.Left();
                break;
            case KeyKind.RightArrow:
                _input.Right();
                break;
            case KeyKind.UpArrow:
                // inline 模式历史由终端滚轮原生回看（基准主屏行为），无视口滚动
                if (!_inline)
                {
                    _view.Messages.ScrollBy(-3);
                }

                break;
            case KeyKind.DownArrow:
                if (!_inline)
                {
                    _view.Messages.ScrollBy(3);
                }

                break;
            case KeyKind.Home:
                _input.Home();
                break;
            case KeyKind.End:
                _input.End();
                break;
            case KeyKind.Char:
                if (key.Input.Length == 1)
                {
                    _input.Insert(key.Input[0]);
                }

                break;
            default:
                // Tab/PageUp/PageDown/Unknown — 输入域 no-op（对齐基准）
                break;
        }
    }

    /// <summary>Ctrl 组合分派（对齐基准 handleCtrl 表）。</summary>
    private void HandleCtrlKey(string name)
    {
        switch (name)
        {
            case "a":
                _input.Home();
                break;
            case "b":
                _input.Left();
                break;
            case "c":
                _running = false;
                break;
            case "d":
                if (_input.Text.Length == 0)
                {
                    _running = false;
                }
                else
                {
                    _input.Delete();
                }

                break;
            case "e":
                _input.End();
                break;
            case "f":
                _input.Right();
                break;
            case "h":
                _input.Backspace();
                break;
            case "k":
                _input.KillToLineEnd();
                break;
            case "n":
                if (!_inline)
                {
                    _view.Messages.ScrollBy(3);
                }

                break;
            case "p":
                if (!_inline)
                {
                    _view.Messages.ScrollBy(-3);
                }

                break;
            case "u":
                _input.KillToLineStart();
                break;
            case "w":
                _input.KillWordBefore();
                break;
            case "y":
                _input.Yank();
                break;
            default:
                break;
        }
    }

    /// <summary>Meta/Alt 组合分派（对齐基准 handleMeta 表）。</summary>
    private void HandleMetaKey(string name)
    {
        switch (name)
        {
            case "b":
                MoveWord(-1);
                break;
            case "f":
                MoveWord(1);
                break;
            case "d":
                _input.KillWordAfter();
                break;
            case "y":
                _input.YankPop();
                break;
            default:
                break;
        }
    }

    /// <summary>词移动统一入口。</summary>
    private void MoveWord(int direction)
    {
        if (direction < 0)
        {
            _input.WordLeft();
        }
        else
        {
            _input.WordRight();
        }
    }

    /// <summary>Esc：忙时打断当前回合；闲时有输入清空（对齐基准双击清空的单击近似），无输入退出。</summary>
    private void HandleEscape()
    {
        if (IsBusy)
        {
            if (_client is not null)
            {
                _client.Interrupt();
                return;
            }

            FinishTurn(interrupted: true);
            return;
        }

        if (_input.Text.Length > 0)
        {
            _input.Clear();
            return;
        }

        _running = false;
    }

    /// <summary>回合进行中（模拟：阶段非空闲；bridge：driver.IsBusy）。</summary>
    private bool IsBusy => _client is null ? _phase != TurnPhase.Idle : _driver!.IsBusy;

    /// <summary>Shift+Tab 循环权限模式（jcc 引擎模式集合）：bridge 模式下发 set_permission_mode 请求、footer 以引擎回执为准；seed 模式本地切换。</summary>
    private void CyclePermissionMode()
    {
        var bypassAvailable = _driver?.BypassAvailable ?? true;
        var next = PermissionModeCycler.Next(_view.Footer.Mode, bypassAvailable);
        if (_client is not null)
        {
            _client.SetPermissionMode(PermissionModeInfo.ToValue(next));
            return;
        }

        _view.Footer = _view.Footer with { Mode = next };
    }

    /// <summary>提交输入：空文本忽略；否则追加用户消息并开始回合（模拟状态机或引擎提交）。</summary>
    private void Submit()
    {
        var text = _input.Text;
        if (text.Length == 0)
        {
            return;
        }

        _input.Clear();
        AddMessage(new UserMessageItem(text));
        if (_client is null)
        {
            StartTurn();
        }
        else
        {
            _driver!.BeginTurn();
            _client.Submit(text);
        }
    }

    /// <summary>开始回合：进入 Thinking 阶段，busy + spinner 启动（消息留空 → 随机动词，对齐基准挂载采样）。</summary>
    private void StartTurn()
    {
        _phase = TurnPhase.Thinking;
        _turnStartMs = Environment.TickCount64;
        _view.Footer = _view.Footer with { Busy = true };
        _spinner.Message = string.Empty;
        _spinner.ResetVerb();
        _spinner.Mode = SpinnerMode.Thinking;
        _spinner.LoadingStartMs = _turnStartMs;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
    }

    /// <summary>推进模拟回合状态机（时间驱动）。</summary>
    private void AdvanceTurn()
    {
        if (_phase == TurnPhase.Idle)
        {
            return;
        }

        var elapsed = Environment.TickCount64 - _turnStartMs;
        if (_phase == TurnPhase.Thinking && elapsed >= 1200)
        {
            _phase = TurnPhase.ToolUse;
            AddMessage(new ToolCallItem("Read", "demo/simulated.cs"));
            _spinner.Message = "Reading files…";
            _spinner.Mode = SpinnerMode.ToolUse;
            _spinner.HasActiveTools = true;
        }

        if (_phase == TurnPhase.ToolUse && elapsed >= 2400)
        {
            _phase = TurnPhase.Responding;
            AddMessage(new ToolResultItem("7 lines (simulated)"));
            _spinner.Message = "Writing response…";
            _spinner.Mode = SpinnerMode.Responding;
            _spinner.HasActiveTools = false;
        }

        if (_phase == TurnPhase.Responding && elapsed >= 5400)
        {
            FinishTurn(interrupted: false);
            return;
        }

        if (_phase == TurnPhase.Responding)
        {
            _spinner.ResponseLength += 9;
        }
    }

    /// <summary>结束回合：正常完成追加助手消息；打断则直接回到空闲。</summary>
    private void FinishTurn(bool interrupted)
    {
        if (!interrupted)
        {
            AddMessage(new AssistantMessageItem("演示回复 — diff 渲染管线运行正常。"));
        }

        _phase = TurnPhase.Idle;
        _view.Footer = _view.Footer with { Busy = false };
        _spinner.Message = string.Empty;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
    }

    /// <summary>渲染一帧并差分上屏：inline 走相对光标 + append（历史滚入 scrollback）；视口走绝对定位 diff。</summary>
    private void RenderAndFlush()
    {
        EnsureScreens();
        _back.Clear();
        _animTimeMs += TickMs;
        _spinner.AnimTimeMs = _animTimeMs;
        _spinner.WallNowMs = Environment.TickCount64;
        _view.Input = _input.Text;
        _view.CursorCol = _input.CursorCol;
        _view.Render(_back, _theme);
        if (_inline)
        {
            var viewportRows = Math.Max(8, Console.WindowHeight);
            var frameEnd = _view.InputCursorPos(_back.Rows, _back.Cols, _input.CursorCol);
            var prev = _frontValid ? _front : _prevOverride;
            var output = _inlineComposer.Compose(prev, _back, viewportRows, _cursor, frameEnd, out _cursor);
            _prevOverride = null;
            if (output.Length > 0)
            {
                Console.Out.Write(Csi.SynchronizedOutputBegin() + output + Csi.SynchronizedOutputEnd());
            }
        }
        else
        {
            var output = _composer.Compose(_frontValid ? _front : null, _back);
            if (output.Length > 0)
            {
                Console.Out.Write(output);
            }

            PositionCursor();
        }

        Console.Out.Flush();
        (_front, _back) = (_back, _front);
        _frontValid = true;
    }

    /// <summary>
    /// 尺寸计算（含首帧）成对重建双缓冲 — inline 屏高 = 内容总高（消息全量展示，可超视口），
    /// 重建时保留旧帧跨尺寸 diff（增长 append / 宽度变 fullReset）；视口模式屏高 = 终端行数。
    /// </summary>
    private void EnsureScreens()
    {
        var cols = Math.Max(20, Console.WindowWidth);
        var rows = _inline
            ? Math.Max(8, _view.MeasureContentHeight(cols))
            : Math.Max(8, Console.WindowHeight);
        if (_back is not null && cols == _back.Cols && rows == _back.Rows)
        {
            return;
        }

        _prevOverride = _inline && _frontValid ? _front : null;
        _front = new Screen(cols, rows, sharedStyles: _pool);
        _back = new Screen(cols, rows, sharedStyles: _pool);
        _frontValid = false;
    }

    /// <summary>退出输出 — inline：清尾部动态区（间隔+输入区+footer），历史留终端 scrollback 可复制；视口：清屏复位。</summary>
    public void WriteExit()
    {
        if (!_inline)
        {
            Console.Out.Write(Sgr.Reset + Csi.EraseScreen() + Csi.CursorPosition(1, 1));
            Console.Out.Flush();
            return;
        }

        // 光标在输入位（上帧 frameEnd）→ 上移 2 行到顶线 → 清 TailHeight 行 → 内容底行换行
        var tail = _view.TailHeight;
        Console.Out.Write(Sgr.Reset + Csi.CursorUp(2) + "\r" + string.Concat(Enumerable.Repeat(Csi.EraseLine() + "\r\n", tail - 1)) + Csi.EraseLine() + "\r\n");
        Console.Out.Flush();
    }

    /// <summary>终端光标落到输入文本行（0 基 Rows-2 → 1 基 Rows-1），列随编辑偏移。</summary>
    private void PositionCursor()
    {
        var row = _back.Rows - 1;
        var col = Math.Min(_back.Cols, PromptInputBox.PromptWidth + _input.CursorCol + 1);
        Console.Out.Write(Csi.CursorPosition(row, col));
    }

    /// <summary>对齐 50ms 拍长（忙完剩余时间睡眠）。</summary>
    private void PaceTick(long tickStart)
    {
        var elapsedMs = _clock.ElapsedMilliseconds - tickStart;
        var sleepMs = TickMs - (int)elapsedMs;
        if (sleepMs > 0)
        {
            Thread.Sleep(sleepMs);
        }
    }
}
