namespace NativeTui.Core.Repl;

/// <summary>
/// 共享 REPL 外壳 — HostSession/DemoSession 提取的统一交互外壳（TASK055 M7 前置，ADR 0143 阶段二）：
/// 主循环每 50ms 一拍：排空键盘 → 排空引擎事件（ReplBridgeDriver 应用到视图）→ 差分上屏 → 帧率对齐。
/// 回合生命周期全事件驱动（TurnStarted 挂 busy，TurnCompleted/TurnInterrupted 解除 — 不乐观预挂）；
/// 引擎交互经 IReplEngine（提交/打断/权限模式），渲染双模式（inline 主屏 log-update / viewport 全屏 diff）；
/// Esc 忙时打断/闲时清输入或退出，Shift+Tab 权限循环（引擎回执为准）。
/// 渲染走注入的独立 writer — 宿主负责隔离引擎内部输出（Host 重定向 stderr）。
/// </summary>
public sealed class ReplShell
{
    /// <summary>动画 tick 周期（基准 spinner setInterval 50ms）。</summary>
    private const int TickMs = 50;

    private readonly Theme _theme = Theme.Dark;
    private readonly ReplView _view = new();
    private readonly InputBuffer _input = new();
    private readonly SpinnerItem _spinner = new() { Message = string.Empty };
    private readonly StylePool _pool = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly InlineComposer _inlineComposer = new();
    private readonly FrameComposer _composer = new();

    /// <summary>引擎交互面（提交/打断/权限/事件排空/存活）。</summary>
    private readonly IReplEngine _engine;

    /// <summary>渲染 writer（宿主注入 — Host 为重定向前捕获的独立 writer）。</summary>
    private readonly TextWriter _output;

    /// <summary>bridge 事件应用器（引擎事件 → 视图状态唯一翻译层）。</summary>
    private readonly ReplBridgeDriver _driver;

    /// <summary>权限对话框协调器（M7-3 — 挂起时键盘模态改道）。</summary>
    private readonly PermissionCoordinator _permission = new();

    /// <summary>斜杠命令补全状态机（TASK056 — 宿主注入命令清单后启用；null = 无补全能力）。</summary>
    private readonly SlashSuggester? _suggester;

    /// <summary>建议列表渲染组件。</summary>
    private readonly SuggestionBox _suggestionBox = new();

    /// <summary>输入历史环（TASK056 — 提交入史 + 上下键翻阅）。</summary>
    private readonly HistoryRing _history = new();

    /// <summary>程序化填充标记（历史导航/补全接受 — HandleKey 尾部跳过建议刷新）。</summary>
    private bool _programmaticFill;

    /// <summary>启动 banner（首项 — 真实引擎由 session_info 事件原地更新）。</summary>
    private readonly BannerItem _banner;

    /// <summary>种子消息（Demo 模拟模式示例内容；真实引擎为空）。</summary>
    private readonly IReadOnlyList<IScrollItem> _seedMessages;

    /// <summary>inline（主屏 log-update）渲染模式 — 默认；false = 视口全屏 diff（旧模式）。</summary>
    private readonly bool _inline;

    private Screen _front = null!;
    private Screen _back = null!;
    private bool _frontValid;
    private long _animTimeMs;
    private bool _running = true;

    /// <summary>inline 模式上一帧尾光标（内容坐标）。</summary>
    private CursorPos _cursor;

    /// <summary>inline 模式尺寸重建后的旧帧（跨尺寸 diff：增长走 append、宽度变走 fullReset）。</summary>
    private Screen? _prevOverride;

    /// <summary>创建外壳 — engine=引擎交互面；banner=启动横幅（首项）；output=渲染 writer；
    /// inline=渲染模式（默认主屏 log-update）；seedMessages=种子消息（banner 之后、spinner 之前）；
    /// commands=斜杠命令清单（宿主注入；null = 无补全能力）。</summary>
    public ReplShell(IReplEngine engine, BannerItem banner, TextWriter output, bool inline = true, IReadOnlyList<IScrollItem>? seedMessages = null, IReadOnlyList<CommandSuggestion>? commands = null)
    {
        _engine = engine;
        _banner = banner;
        _output = output;
        _inline = inline;
        _seedMessages = seedMessages ?? [];
        _suggester = commands is { Count: > 0 } ? new SlashSuggester(commands) : null;
        _driver = new ReplBridgeDriver(_view, _spinner, _banner, BeginPermissionRequest);
    }

    /// <summary>视图（测试断言面）。</summary>
    internal ReplView View => _view;

    /// <summary>输入缓冲（测试注入面）。</summary>
    internal InputBuffer Input => _input;

    /// <summary>回合进行中（turn_started 后未 completed/interrupted — 事件驱动）。</summary>
    internal bool IsBusy => _driver.IsBusy;

    /// <summary>主循环运行标记（false = Esc/Ctrl+C/引擎死亡请求退出）。</summary>
    internal bool Running => _running;

    /// <summary>权限对话框协调器（宿主权限桥 Begin/Wait 消费面 + 测试断言面）。</summary>
    public PermissionCoordinator Permission => _permission;

    /// <summary>斜杠补全状态机（测试断言面；null = 无命令清单）。</summary>
    internal SlashSuggester? Suggester => _suggester;

    /// <summary>输入历史环（测试断言面）。</summary>
    internal HistoryRing History => _history;

    /// <summary>历史种子注入（宿主启动时从 transcript 加载最近输入）。</summary>
    public void SeedHistory(IEnumerable<string> entries) => _history.Seed(entries);

    /// <summary>主循环 — 每拍：排空键盘 → 排空引擎事件 → 差分上屏 → 对齐 50ms。</summary>
    public void Run()
    {
        SeedContent();
        while (_running)
        {
            var tickStart = _clock.ElapsedMilliseconds;
            DrainInput();
            DrainEngine();
            RenderAndFlush();
            PaceTick(tickStart);
        }
    }

    /// <summary>初始内容 — footer 种子 + banner 首项 + 种子消息 + 常驻 spinner（滚动区末项）；
    /// footer 权限模式为占位值，真实引擎由首拍 session_info 事件覆盖。</summary>
    internal void SeedContent()
    {
        _view.Footer = new FooterState
        {
            Mode = PermissionMode.Auto,
            AgentsHint = "← agents",
            LoginError = "未登录 · 运行 /login",
            Notification = $"{Figures.EffortMedium} 中等 · /effort",
        };
        _view.Messages.Add(_banner);
        foreach (var message in _seedMessages)
        {
            _view.Messages.Add(message);
        }

        _view.Messages.Add(_spinner);
    }

    /// <summary>排空引擎事件：全部经 ReplBridgeDriver 应用到视图 + 诊断旁路写 stderr + 引擎死亡检测（非忙时追加退出消息并结束）。</summary>
    internal void DrainEngine()
    {
        foreach (var bridgeEvent in _engine.DrainEvents())
        {
            _driver.Apply(bridgeEvent);
        }

        foreach (var diagnostic in _engine.DrainDiagnostics())
        {
            Console.Error.WriteLine(diagnostic);
        }

        if (!_engine.IsAlive && !_driver.IsBusy)
        {
            AddMessage(new ErrorMessageItem("引擎进程已退出，会话结束。"));
            _running = false;
        }

        // 回合结束（完成/打断/异常）时挂起中的权限对话框自动收尾 — 引擎不会再等用户，Deny 关闭避免对话框残留
        if (!_driver.IsBusy && _permission.IsPending)
        {
            CompletePermission(PermissionChoice.Deny);
        }
    }

    /// <summary>挂起权限请求（宿主 TuiPermissionBridge.Confirm → 此处 → 用户按键 Complete）。</summary>
    public void BeginPermissionRequest(string toolName, string description)
    {
        _permission.Begin(new PermissionRequest(toolName, description));
        AddMessage(new PermissionRequestItem(_permission));
    }

    /// <summary>完成权限请求并移除对话框项。</summary>
    private void CompletePermission(PermissionChoice choice)
    {
        _permission.Complete(choice);
        for (var i = 0; i < _view.Messages.Count; i++)
        {
            if (_view.Messages.Items[i] is PermissionRequestItem)
            {
                _view.Messages.RemoveAt(i);
                return;
            }
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

    /// <summary>按键归一 + 分派（对齐基准 useTextInput.mapKey 优先级顺序 — 测试经 ConsoleKeyInfo 全链注入）；
    /// 文本变化才刷新建议（导航/移动键不重置选中索引；程序化填充跳过 — 历史导航不触发补全）。</summary>
    internal void HandleKey(ConsoleKeyInfo keyInfo)
    {
        var key = ConsoleKeyAdapter.ToKeyPress(keyInfo);
        if (key is null)
        {
            return;
        }

        var textBefore = _input.Text;
        HandleKeyPress(key);
        if (_programmaticFill)
        {
            _programmaticFill = false;
            return;
        }

        if (_input.Text != textBefore)
        {
            UpdateSuggestions();
        }
    }

    /// <summary>按键分派 — 权限对话框模态优先（数字/Esc/上下/Enter 路由对话框）→ 补全激活专属键
    /// （Up/Down 导航/Tab 接受/Enter 执行/Esc 关闭，基准 Autocomplete 上下文优先于 Chat），
    /// 其余 Emacs 编辑（Ctrl/Meta 词操作 + kill ring），控制键走编辑/提交/权限，字符插入。</summary>
    private void HandleKeyPress(KeyPress key)
    {
        if (_permission.IsPending)
        {
            HandlePermissionKey(key);
            return;
        }

        if (HandleSuggestionKey(key))
        {
            return;
        }

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
                NavigateHistory(-1);
                break;
            case KeyKind.DownArrow:
                NavigateHistory(1);
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
                ScrollIfViewport(3);
                break;
            case "p":
                ScrollIfViewport(-3);
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

    /// <summary>
    /// 权限对话框模态按键（M7-3，对齐基准 CustomSelect useSelectInput）：
    /// 数字 1/2/3 直接选档（yes/dont-ask-again/no）+ Esc 取消（Deny）+ Enter 选焦点档 + 上/下环绕移动焦点；
    /// 其余按键全部吞掉（模态 — 不进输入缓冲、不打断回合、不退出）。
    /// </summary>
    private void HandlePermissionKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Escape)
        {
            CompletePermission(PermissionChoice.Deny);
            return;
        }

        if (key.Kind == KeyKind.Return)
        {
            CompletePermission((PermissionChoice)_permission.FocusIndex);
            return;
        }

        if (key.Kind == KeyKind.UpArrow)
        {
            _permission.MoveFocus(-1);
            return;
        }

        if (key.Kind == KeyKind.DownArrow)
        {
            _permission.MoveFocus(1);
            return;
        }

        if (key is { Kind: KeyKind.Char, Ctrl: false, Meta: false, Shift: false } && key.Input is "1" or "2" or "3")
        {
            CompletePermission((PermissionChoice)(key.Input[0] - '1'));
        }
    }

    /// <summary>视口模式滚动（inline 模式历史由终端滚轮原生回看，no-op）。</summary>
    private void ScrollIfViewport(int rows)
    {
        if (!_inline)
        {
            _view.Messages.ScrollBy(rows);
        }
    }

    /// <summary>补全激活时的专属键拦截（基准 Autocomplete 上下文）：
    /// Up/Down 循环导航、Tab 接受不执行、Enter 接受并提交、Esc 仅关建议（优先于打断/清输入/退出）；
    /// 其余键返回 false 走正常编辑路径（编辑后刷新建议）。</summary>
    private bool HandleSuggestionKey(KeyPress key)
    {
        if (_suggester is not { IsActive: true } suggester)
        {
            return false;
        }

        switch (key.Kind)
        {
            case KeyKind.UpArrow:
                suggester.Move(-1);
                return true;
            case KeyKind.DownArrow:
                suggester.Move(1);
                return true;
            case KeyKind.Tab:
                AcceptSuggestion(execute: false);
                return true;
            case KeyKind.Return:
                AcceptSuggestion(execute: true);
                return true;
            case KeyKind.Escape:
                suggester.Dismiss();
                return true;
            default:
                return false;
        }
    }

    /// <summary>接受选中建议（基准 applyCommandSuggestion）— Tab 补全为 "/命令 "（带尾随空格进入参数态，不执行）；
    /// Enter 直接提交 "/命令"（不带尾随空格，历史与解析干净）。</summary>
    private void AcceptSuggestion(bool execute)
    {
        if (_suggester?.Selected is not { } selected)
        {
            return;
        }

        FillInput(execute ? $"/{selected.Name}" : $"/{selected.Name} ", cursorToStart: false);
        UpdateSuggestions();
        if (execute)
        {
            Submit();
        }
    }

    /// <summary>历史导航（inline 主模式）— 上=翻旧（光标置行首，基准 cursorToStart）、下=翻新（恢复草稿光标置末尾）；
    /// 到底/到顶保持不动（不回绕）；viewport 旧模式保留滚动语义。</summary>
    private void NavigateHistory(int direction)
    {
        if (!_inline)
        {
            ScrollIfViewport(direction * 3);
            return;
        }

        string text;
        if (direction < 0)
        {
            if (!_history.TryUp(out text, _input.Text))
            {
                return;
            }
        }
        else if (!_history.TryDown(out text))
        {
            return;
        }

        FillInput(text, cursorToStart: direction < 0);
    }

    /// <summary>程序化填充输入（历史导航/补全接受）— 设置跳过标记，HandleKey 尾部不刷新建议。</summary>
    private void FillInput(string text, bool cursorToStart)
    {
        _programmaticFill = true;
        _input.Replace(text, cursorToStart);
    }

    /// <summary>刷新建议（用户编辑路径；建议状态机内部判定触发条件）。</summary>
    private void UpdateSuggestions() => _suggester?.Update(_input.Text);

    /// <summary>Esc：忙时打断当前回合；闲时有输入清空（对齐基准双击清空的单击近似），无输入退出。</summary>
    internal void HandleEscape()
    {
        if (IsBusy)
        {
            _engine.Interrupt();
            return;
        }

        if (_input.Text.Length > 0)
        {
            _input.Clear();
            return;
        }

        _running = false;
    }

    /// <summary>Shift+Tab 循环权限模式（jcc 引擎模式集合）— 发引擎应用请求，footer 以 permission_mode_changed 回执为准。</summary>
    internal void CyclePermissionMode()
    {
        var next = PermissionModeCycler.Next(_view.Footer.Mode, _driver.BypassAvailable);
        _engine.SetPermissionMode(PermissionModeInfo.ToValue(next));
    }

    /// <summary>提交输入：空文本忽略；否则追加用户消息并提交引擎（TurnStarted 事件驱动 busy，忙时由实现拒绝并 Error 诱导）；
    /// 提交文本入历史环（末条去重）+ 导航态复位。</summary>
    internal void Submit()
    {
        var text = _input.Text;
        if (text.Length == 0)
        {
            return;
        }

        _input.Clear();
        _history.Add(text);
        _history.Reset();
        AddMessage(new UserMessageItem(text));
        _engine.Submit(text);
    }

    /// <summary>渲染一帧并差分上屏：inline 走相对光标 + append（历史滚入 scrollback）+ DEC 2026 原子上屏；视口走绝对定位 diff。</summary>
    private void RenderAndFlush()
    {
        SyncSuggestionState();
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
                _output.Write(Csi.SynchronizedOutputBegin() + output + Csi.SynchronizedOutputEnd());
            }
        }
        else
        {
            var output = _composer.Compose(_frontValid ? _front : null, _back);
            if (output.Length > 0)
            {
                _output.Write(output);
            }

            PositionCursor();
        }

        _output.Flush();
        (_front, _back) = (_back, _front);
        _frontValid = true;
    }

    /// <summary>同步补全建议到视图（渲染前状态投影 — EnsureScreens 屏高计算依赖建议区高度，必须先行）。</summary>
    private void SyncSuggestionState()
    {
        _view.Suggestions = _suggester is { IsActive: true } ? _suggester.Items : null;
        _view.SuggestionIndex = _suggester?.SelectedIndex ?? 0;
        _view.SuggestionNameColumnWidth = _suggester?.NameColumnWidth ?? 0;
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
            _output.Write(Sgr.Reset + Csi.EraseScreen() + Csi.CursorPosition(1, 1));
            _output.Flush();
            return;
        }

        // 光标在输入位（上帧 frameEnd）→ 上移 2 行到顶线 → 清 TailHeight 行 → 内容底行换行
        var tail = _view.TailHeight;
        _output.Write(Sgr.Reset + Csi.CursorUp(2) + "\r" + string.Concat(Enumerable.Repeat(Csi.EraseLine() + "\r\n", tail - 1)) + Csi.EraseLine() + "\r\n");
        _output.Flush();
    }

    /// <summary>终端光标落到输入文本行（视口模式 — 0 基 Rows-2 → 1 基 Rows-1），列随编辑偏移。</summary>
    private void PositionCursor()
    {
        var row = _back.Rows - 1;
        var col = Math.Min(_back.Cols, PromptInputBox.PromptWidth + _input.CursorCol + 1);
        _output.Write(Csi.CursorPosition(row, col));
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
