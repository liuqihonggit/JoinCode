namespace NativeTui.Host;

/// <summary>
/// 宿主 REPL 会话 — DemoSession bridge 模式的进程内直连版（ADR 0143 阶段二，inline 渲染独占）：
/// 主循环每 50ms 一拍：排空键盘 → 排空引擎事件（ReplBridgeDriver 应用到视图）→ inline 差分上屏 → 帧率对齐。
/// 回合生命周期全事件驱动：TurnStarted 事件驱动 BeginTurn（进程内零延迟，无需乐观预挂 busy），
/// 忙时提交由 EngineTurnClient 拒绝（Error 诱导），Esc 忙时打断/闲时清输入或退出，Shift+Tab 权限循环（回执为准）。
/// 渲染走注入的独立 stdout writer — 引擎内部输出已被 Program 重定向 stderr，不污染渲染流。
/// </summary>
internal sealed class HostSession {
    /// <summary>动画 tick 周期（基准 spinner setInterval 50ms）。</summary>
    private const int TickMs = 50;

    private readonly Theme _theme = Theme.Dark;
    private readonly ReplView _view = new();
    private readonly InputBuffer _input = new();
    private readonly SpinnerItem _spinner = new() { Message = string.Empty };
    private readonly StylePool _pool = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly InlineComposer _inlineComposer = new();

    /// <summary>引擎回合客户端（Submit/Interrupt/SetPermissionMode/DrainEvents）。</summary>
    private readonly EngineTurnClient _client;

    /// <summary>独立渲染 writer（Console.Out 重定向前捕获 — 引擎内部输出走 stderr）。</summary>
    private readonly TextWriter _output;

    /// <summary>bridge 事件应用器（引擎事件 → 视图状态唯一翻译层）。</summary>
    private readonly ReplBridgeDriver _driver;

    /// <summary>启动 banner（首项 — 首拍 DrainEngine 应用 session_info 事件更新引擎真实数据）。</summary>
    private readonly BannerItem _banner;

    private Screen _front = null!;
    private Screen _back = null!;
    private bool _frontValid;
    private long _animTimeMs;
    private bool _running = true;

    /// <summary>inline 模式上一帧尾光标（内容坐标）。</summary>
    private CursorPos _cursor;

    /// <summary>inline 模式尺寸重建后的旧帧（跨尺寸 diff：增长走 append、宽度变走 fullReset）。</summary>
    private Screen? _prevOverride;

    /// <summary>创建会话 — client=引擎回合客户端；output=独立渲染 writer。</summary>
    public HostSession(EngineTurnClient client, TextWriter output) {
        _client = client;
        _output = output;
        _banner = new BannerItem("0.0.0", "当前模型未设置", "API 按量计费", Environment.CurrentDirectory);
        _driver = new ReplBridgeDriver(_view, _spinner, _banner);
    }

    /// <summary>主循环 — 每拍：排空键盘 → 排空引擎事件 → inline 差分上屏 → 对齐 50ms。</summary>
    public void Run() {
        SeedContent();
        while (_running) {
            var tickStart = _clock.ElapsedMilliseconds;
            DrainInput();
            DrainEngine();
            RenderAndFlush();
            PaceTick(tickStart);
        }
    }

    /// <summary>初始内容 — 启动 banner + 常驻 spinner（滚动区留空，回合消息全事件驱动）；
    /// footer 权限模式为占位值，首拍 session_info 事件覆盖为引擎实际值。</summary>
    private void SeedContent() {
        _view.Footer = new FooterState {
            Mode = PermissionMode.Auto,
            AgentsHint = "← agents",
            LoginError = "未登录 · 运行 /login",
            Notification = $"{Figures.EffortMedium} 中等 · /effort",
        };
        _view.Messages.Add(_banner);
        _view.Messages.Add(_spinner);
    }

    /// <summary>排空引擎事件：全部经 ReplBridgeDriver 应用到视图（回合错误以 Error 事件到达，进程内引擎无退出检测）。</summary>
    private void DrainEngine() {
        foreach (var bridgeEvent in _client.DrainEvents())
            _driver.Apply(bridgeEvent);
    }

    /// <summary>新消息插入到常驻 spinner 之前（spinner 恒为滚动区最后一项）。</summary>
    private void AddMessage(IScrollItem item) =>
        _view.Messages.Insert(_view.Messages.Count - 1, item);

    /// <summary>排空键盘事件（非阻塞）— ReadKey 归一 KeyPress 后统一分派。</summary>
    private void DrainInput() {
        while (_running && Console.KeyAvailable)
            HandleKey(Console.ReadKey(intercept: true));
    }

    /// <summary>按键归一 + 分派（对齐基准 useTextInput.mapKey 优先级顺序）。</summary>
    private void HandleKey(ConsoleKeyInfo keyInfo) {
        var key = ConsoleKeyAdapter.ToKeyPress(keyInfo);
        if (key is null)
            return;

        HandleKeyPress(key);
    }

    /// <summary>按键分派 — Emacs 编辑（Ctrl/Meta 词操作 + kill ring）优先，控制键走编辑/提交/权限，字符插入。</summary>
    private void HandleKeyPress(KeyPress key) {
        if (key.Kind == KeyKind.Escape) {
            HandleEscape();
            return;
        }

        if (key.Ctrl && key.Kind is KeyKind.LeftArrow or KeyKind.RightArrow) {
            MoveWord(key.Kind == KeyKind.LeftArrow ? -1 : 1);
            return;
        }

        if (key.Kind == KeyKind.Backspace) {
            if (key.Meta || key.Ctrl) {
                _input.KillWordBefore();
            } else {
                _input.Backspace();
            }

            return;
        }

        if (key.Kind == KeyKind.Delete) {
            if (key.Meta) {
                _input.KillToLineEnd();
            } else {
                _input.Delete();
            }

            return;
        }

        if (key.Ctrl && key.Kind == KeyKind.Char) {
            HandleCtrlKey(key.Input);
            return;
        }

        if (key.Meta && key.Kind == KeyKind.Char) {
            HandleMetaKey(key.Input);
            return;
        }

        switch (key.Kind) {
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
            case KeyKind.DownArrow:
                // inline 模式历史由终端滚轮原生回看（基准主屏行为），无视口滚动
                break;
            case KeyKind.Home:
                _input.Home();
                break;
            case KeyKind.End:
                _input.End();
                break;
            case KeyKind.Char:
                if (key.Input.Length == 1) {
                    _input.Insert(key.Input[0]);
                }

                break;
            default:
                // Tab/PageUp/PageDown/Unknown — 输入域 no-op（对齐基准）
                break;
        }
    }

    /// <summary>Ctrl 组合分派（对齐基准 handleCtrl 表）。</summary>
    private void HandleCtrlKey(string name) {
        switch (name) {
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
                if (_input.Text.Length == 0) {
                    _running = false;
                } else {
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
    private void HandleMetaKey(string name) {
        switch (name) {
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
    private void MoveWord(int direction) {
        if (direction < 0) {
            _input.WordLeft();
        } else {
            _input.WordRight();
        }
    }

    /// <summary>Esc：忙时打断当前回合；闲时有输入清空（对齐基准双击清空的单击近似），无输入退出。</summary>
    private void HandleEscape() {
        if (IsBusy) {
            _client.Interrupt();
            return;
        }

        if (_input.Text.Length > 0) {
            _input.Clear();
            return;
        }

        _running = false;
    }

    /// <summary>回合进行中（turn_started 后未 completed/interrupted — 事件驱动）。</summary>
    private bool IsBusy => _driver.IsBusy;

    /// <summary>Shift+Tab 循环权限模式（jcc 引擎模式集合）— 发引擎应用请求，footer 以 permission_mode_changed 回执为准。</summary>
    private void CyclePermissionMode() {
        var next = PermissionModeCycler.Next(_view.Footer.Mode, _driver.BypassAvailable);
        _client.SetPermissionMode(PermissionModeInfo.ToValue(next));
    }

    /// <summary>提交输入：空文本忽略；否则追加用户消息并提交引擎（TurnStarted 事件驱动 busy，忙时由 client 拒绝并 Error 诱导）。</summary>
    private void Submit() {
        var text = _input.Text;
        if (text.Length == 0)
            return;

        _input.Clear();
        AddMessage(new UserMessageItem(text));
        _client.Submit(text);
    }

    /// <summary>渲染一帧并 inline 差分上屏（相对光标 append，历史滚入终端 scrollback）+ DEC 2026 原子上屏。</summary>
    private void RenderAndFlush() {
        EnsureScreens();
        _back.Clear();
        _animTimeMs += TickMs;
        _spinner.AnimTimeMs = _animTimeMs;
        _spinner.WallNowMs = Environment.TickCount64;
        _view.Input = _input.Text;
        _view.CursorCol = _input.CursorCol;
        _view.Render(_back, _theme);

        var viewportRows = Math.Max(8, Console.WindowHeight);
        var frameEnd = _view.InputCursorPos(_back.Rows, _back.Cols, _input.CursorCol);
        var prev = _frontValid ? _front : _prevOverride;
        var output = _inlineComposer.Compose(prev, _back, viewportRows, _cursor, frameEnd, out _cursor);
        _prevOverride = null;
        if (output.Length > 0) {
            _output.Write(Csi.SynchronizedOutputBegin() + output + Csi.SynchronizedOutputEnd());
        }

        _output.Flush();
        (_front, _back) = (_back, _front);
        _frontValid = true;
    }

    /// <summary>
    /// 尺寸计算（含首帧）成对重建双缓冲 — inline 屏高 = 内容总高（消息全量展示，可超视口），
    /// 重建时保留旧帧跨尺寸 diff（增长 append / 宽度变 fullReset）。
    /// </summary>
    private void EnsureScreens() {
        var cols = Math.Max(20, Console.WindowWidth);
        var rows = Math.Max(8, _view.MeasureContentHeight(cols));
        if (_back is not null && cols == _back.Cols && rows == _back.Rows)
            return;

        _prevOverride = _frontValid ? _front : null;
        _front = new Screen(cols, rows, sharedStyles: _pool);
        _back = new Screen(cols, rows, sharedStyles: _pool);
        _frontValid = false;
    }

    /// <summary>退出输出 — inline：清尾部动态区（间隔+输入区+footer），历史留终端 scrollback 可复制。</summary>
    public void WriteExit() {
        // 光标在输入位（上帧 frameEnd）→ 上移 2 行到顶线 → 清 TailHeight 行 → 内容底行换行
        var tail = _view.TailHeight;
        _output.Write(Sgr.Reset + Csi.CursorUp(2) + "\r" + string.Concat(Enumerable.Repeat(Csi.EraseLine() + "\r\n", tail - 1)) + Csi.EraseLine() + "\r\n");
        _output.Flush();
    }

    /// <summary>对齐 50ms 拍长（忙完剩余时间睡眠）。</summary>
    private void PaceTick(long tickStart) {
        var elapsedMs = _clock.ElapsedMilliseconds - tickStart;
        var sleepMs = TickMs - (int)elapsedMs;
        if (sleepMs > 0) {
            Thread.Sleep(sleepMs);
        }
    }
}
