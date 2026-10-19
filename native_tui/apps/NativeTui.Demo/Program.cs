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
    /// <summary>入口 — 分发自检/交互模式。</summary>
    public static int Main(string[] args) =>
        args is ["--selftest"] ? RunSelftest() : RunInteractive();

    /// <summary>交互模式：进 raw mode 跑主循环，退出时清屏复位。</summary>
    private static int RunInteractive()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("交互模式需要真实终端（stdin/stdout 被重定向）。请直接在终端运行 NativeTui.Demo.exe，或用 --selftest 跑非交互冒烟。");
            return 1;
        }

        Console.OutputEncoding = Encoding.UTF8;
        using var raw = new RawConsoleMode();
        new DemoSession().Run();
        Console.Out.Write(Sgr.Reset + Csi.EraseScreen() + Csi.CursorPosition(1, 1));
        Console.Out.Flush();
        Console.WriteLine("NativeTui.Demo 已退出。");
        return 0;
    }

    /// <summary>自检模式：三组冒烟断言（无终端依赖，CI 可跑）。</summary>
    private static int RunSelftest()
    {
        var failures = new List<string>();
        SelftestSpinnerChain(failures);
        SelftestStaticZeroOutput(failures);
        SelftestInputBuffer(failures);
        SelftestKeyPressParser(failures);
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.Error.WriteLine("FAIL: " + failure);
            }

            return 1;
        }

        Console.WriteLine("SELFTEST OK — spinner 动画链路 / 静态零输出 / 输入编辑 全部通过");
        return 0;
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
/// demo 会话 — 持有全部运行时状态（视图/输入/双缓冲/模拟回合状态机），
/// 主循环每 50ms 一拍：排空键盘 → 推进模拟 → 渲染差分上屏 → 帧率对齐。
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

    private Screen _front = null!;
    private Screen _back = null!;
    private bool _frontValid;
    private long _animTimeMs;
    private TurnPhase _phase = TurnPhase.Idle;
    private long _turnStartMs;
    private bool _running = true;

    /// <summary>主循环 — 每拍：排空键盘 → 推进模拟 → 渲染差分上屏 → 对齐 50ms。</summary>
    public void Run()
    {
        SeedContent();
        while (_running)
        {
            var tickStart = _clock.ElapsedMilliseconds;
            DrainInput();
            AdvanceTurn();
            RenderAndFlush();
            PaceTick(tickStart);
        }
    }

    /// <summary>初始内容 — 几条示例消息 + spinner 常驻滚动区尾部。</summary>
    private void SeedContent()
    {
        _view.Messages.Add(new UserMessageItem("这个 TUI 是像素级对齐 Claude Code 的自研实现吗？"));
        _view.Messages.Add(new AssistantMessageItem("是的 — 纯 C# 自研渲染管线：Screen cell 网格 + CellDiffer 差分 + DEC 2026 原子上屏。"));
        _view.Messages.Add(new ToolCallItem("Read", "native_tui/src/NativeTui.Core/repl/ReplView.cs"));
        _view.Messages.Add(new ToolResultItem("41 行 — REPL 整屏视图（footer + 输入框 + 消息滚动区）"));
        _view.Messages.Add(_spinner);
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
            case KeyKind.LeftArrow:
                _input.Left();
                break;
            case KeyKind.RightArrow:
                _input.Right();
                break;
            case KeyKind.UpArrow:
                _view.Messages.ScrollBy(-3);
                break;
            case KeyKind.DownArrow:
                _view.Messages.ScrollBy(3);
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
                _view.Messages.ScrollBy(3);
                break;
            case "p":
                _view.Messages.ScrollBy(-3);
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
        if (_phase != TurnPhase.Idle)
        {
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

    /// <summary>提交输入：空文本忽略；否则追加用户消息并开始模拟回合。</summary>
    private void Submit()
    {
        var text = _input.Text;
        if (text.Length == 0)
        {
            return;
        }

        _input.Clear();
        AddMessage(new UserMessageItem(text));
        StartTurn();
    }

    /// <summary>开始回合：进入 Thinking 阶段，busy + spinner 启动。</summary>
    private void StartTurn()
    {
        _phase = TurnPhase.Thinking;
        _turnStartMs = Environment.TickCount64;
        _view.Busy = true;
        _spinner.Message = "Thinking…";
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
        _view.Busy = false;
        _spinner.Message = string.Empty;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
    }

    /// <summary>渲染一帧并差分上屏：双缓冲 ping-pong，尺寸变化/首帧走全量，光标落输入位。</summary>
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
        var output = _composer.Compose(_frontValid ? _front : null, _back);
        if (output.Length > 0)
        {
            Console.Out.Write(output);
        }

        PositionCursor();
        Console.Out.Flush();
        (_front, _back) = (_back, _front);
        _frontValid = true;
    }

    /// <summary>尺寸变化（含首帧）成对重建双缓冲；全量标志触发首帧 EraseScreen。</summary>
    private void EnsureScreens()
    {
        var cols = Math.Max(20, Console.WindowWidth);
        var rows = Math.Max(8, Console.WindowHeight);
        if (_back is not null && cols == _back.Cols && rows == _back.Rows)
        {
            return;
        }

        _front = new Screen(cols, rows, sharedStyles: _pool);
        _back = new Screen(cols, rows, sharedStyles: _pool);
        _frontValid = false;
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
