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
    private readonly SpinnerItem _spinner = new() { Message = string.Empty, Hidden = true };
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

    /// <summary>模型选择器协调器（M7-6 — 挂起时键盘模态改道，优先级低于权限对话框）。</summary>
    private readonly ModelPickerCoordinator _modelPicker = new();

    /// <summary>模型选择完成回调（BeginModelPicker 注入；参数 = 选中模型 id，Esc 取消为 null）。</summary>
    private Action<string?>? _modelPickerCallback;

    /// <summary>斜杠命令补全状态机（TASK056 — 宿主注入命令清单后启用；null = 无补全能力）。</summary>
    private readonly SlashSuggester? _suggester;

    /// <summary>终端尺寸源（resize 事件化注入面 — 每拍取尺寸，变化触发屏重建跨尺寸 diff）。</summary>
    private readonly ITerminalSizeSource _sizeSource;

    /// <summary>建议列表渲染组件。</summary>
    private readonly SuggestionBox _suggestionBox = new();

    /// <summary>输入历史环（TASK056 — 提交入史 + 上下键翻阅）。</summary>
    private readonly HistoryRing _history = new();

    /// <summary>程序化填充标记（历史导航/补全接受 — HandleKey 尾部跳过建议刷新）。</summary>
    private bool _programmaticFill;

    /// <summary>历史反向搜索态（Ctrl+R — TASK057-4；null = 未激活）。</summary>
    private HistorySearch? _search;

    /// <summary>忙时排队输入队列（TASK060 — 基准 queued 语义，回合结束逐条自动发送）。</summary>
    private readonly List<string> _queuedInputs = [];

    /// <summary>排队输入条数上限（守卫 — 狂敲防队列无限膨胀：满后新输入丢弃带反馈）。</summary>
    internal const int MaxQueuedInputs = 10;

    /// <summary>消息光标导航协调器（交互层 Step1 — 第四交互态，优先级低于权限/模型/搜索模态）。</summary>
    private readonly MessageCursor _cursorNav = new();

    /// <summary>进入搜索前的输入草稿（Esc 退出恢复）。</summary>
    private string _preSearchDraft = string.Empty;

    /// <summary>上次 Esc 按下时间戳（ms — 双击窗口判定；0 = 无先前击）。</summary>
    private long _lastEscapeMs;

    /// <summary>Esc 双击窗口时长（ms — 窗口内两击构成双击中断）。</summary>
    internal const int EscapeDoubleClickWindowMs = 500;

    /// <summary>ctrl+d 退出双击确认窗口时长（ms — S6 任务 6 对齐基准 useExitOnCtrlCD 双击确认；
    /// 窗口内二次空输入 ctrl+d 才退出，通知提示与窗口同长自然到期清除）。</summary>
    internal const int ExitConfirmWindowMs = 3000;

    /// <summary>上次 ctrl+d 退出确认按下时间戳（ms — 0 = 无先前击；空输入门槛由调用路径保证）。</summary>
    private long _lastCtrlDExitMs;

    /// <summary>ctrl+d 退出确认链（S6 任务 6 — 基准 useExitOnCtrlCD/useDoublePress 语义）：
    /// 窗口内二次按下 → 退出；首次按下 → 通知提示"再按一次 ctrl+d 退出"（3s 到期自动清除 = 窗口过期）；
    /// 空输入门槛由 HandleCtrlKey "d" 分支保证（有文本时 ctrl+d 是删除字符）。
    /// 时钟复用 EscClock 注入面（测试可控时间戳）。</summary>
    private void HandleExitCtrlD()
    {
        var now = EscClock();
        if (_lastCtrlDExitMs != 0 && now - _lastCtrlDExitMs <= ExitConfirmWindowMs)
        {
            _lastCtrlDExitMs = 0;
            _running = false;
            return;
        }

        _lastCtrlDExitMs = now;
        _notifications.Enqueue("再按一次 ctrl+d 退出", timeout: TimeSpan.FromMilliseconds(ExitConfirmWindowMs));
    }

    /// <summary>Esc 时钟源（internal — 测试注入可控时间戳；默认墙钟）。</summary>
    internal Func<long> EscClock { get; set; } = static () => Environment.TickCount64;

    /// <summary>最近一次直发提交的文本（M8-2 打断恢复 — 无有效响应时弹回输入框）。</summary>
    private string _lastSubmittedText = string.Empty;

    /// <summary>最近一次直发提交的用户消息项位置（M8-2 打断恢复 — 无有效响应时移除该条上屏）。</summary>
    private int _lastSubmittedIndex = -1;

    /// <summary>帮助面板开关（S6 任务 5 — 空闲态 '?' 弹出；分派链插在 suggestion/transcript 之后）。</summary>
    private bool _helpOpen;

    /// <summary>帮助面板态（internal — 测试断言面）。</summary>
    internal bool IsHelpOpen => _helpOpen;

    /// <summary>ctrl+o 全局 transcript 模式引擎（M8-3/S6 收敛 — S5 TranscriptController 唯一实现）：
    /// 快照提供器拍摄消息全文行投影 + verbose 回调接 ReplView.SetVerboseMode；
    /// 分派/视口/footer 投影/Tick 由本外壳接线（详见 HandleTranscriptKey/SyncTranscriptView）。</summary>
    private readonly TranscriptController _transcript;

    /// <summary>transcript 模式态（internal — 测试断言面）。</summary>
    internal bool IsTranscriptMode => _transcript.IsActive;

    /// <summary>transcript 控制器（internal — 测试断言面：快照/滚动/搜索状态机）。</summary>
    internal TranscriptController Transcript => _transcript;

    /// <summary>令牌用量记账器（S4 产出 — Driver 构造注入：引擎 token_usage 事件经 Driver 路由 RecordUsage；
    /// footer 右侧 TokenWarning 渲染源，FooterState.Tokens 接线）。</summary>
    private readonly TokenUsageTracker _tokenUsage = new();

    /// <summary>动态通知队列（S4 产出 — footer 行 2 右侧渲染源；帧拍 Tick 推进到期/出队，
    /// 种子 effort 提示经 Enqueue 一次性入队保留首屏语义）。</summary>
    private readonly ReplNotifications _notifications = new();

    /// <summary>剪贴板写入委托（TASK059 — Ctrl+C 复制最后 assistant 消息；宿主注入引擎
    /// IClipboardService 适配，Core 零依赖不持接口；null = 复制能力降级无操作）。</summary>
    private readonly Action<string>? _copyToClipboard;

    /// <summary>主循环存活标记（internal — 退出安全测试断言面：Esc/Ctrl+C 永不置 false）。</summary>
    internal bool IsRunning => _running;

    /// <summary>登录门禁开（driver 投影 — 引擎 session_info 报告；false 时提交本地反馈不调引擎）。</summary>
    internal bool LoginGateOpen => _driver.IsLoggedIn;

    /// <summary>启动 banner（首项 — 真实引擎由 session_info 事件原地更新）。</summary>
    private readonly BannerItem _banner;

    /// <summary>种子消息（Demo 模拟模式示例内容；真实引擎为空）。</summary>
    private readonly IReadOnlyList<IScrollItem> _seedMessages;

    /// <summary>inline（主屏 log-update）渲染模式 — 默认；false = 视口全屏 diff（旧模式）。</summary>
    private readonly bool _inline;

    private Screen _front = null!;
    private Screen _back = null!;
    private bool _frontValid;

    /// <summary>强制全量重绘标记（S6 任务 8 — ctrl+l 清 Screen 下一帧全量重绘；消费后复位）。</summary>
    private bool _forceFullReset;

    /// <summary>强制重绘空白前置帧（零尺寸屏 — InlineComposer 判 prev.Cols ≠ next.Cols 走
    /// FullReset 路径：光标移视口顶 + EraseScreen + 重绘可见区；缓存复用零分配）。</summary>
    private Screen? _blankPrev;
    private long _animTimeMs;
    private volatile bool _running = true;

    /// <summary>滚动区已请求清屏标记（M8-5 — /clear 回调置位，主循环渲染前消费；volatile 跨线程）。</summary>
    private volatile bool _screenClearPending;

    /// <summary>inline 模式上一帧尾光标（内容坐标）。</summary>
    private CursorPos _cursor;

    /// <summary>inline 模式尺寸重建后的旧帧（跨尺寸 diff：增长走 append、宽度变走 fullReset）。</summary>
    private Screen? _prevOverride;

    /// <summary>创建外壳 — engine=引擎交互面；banner=启动横幅（首项）；output=渲染 writer；
    /// inline=渲染模式（默认主屏 log-update）；seedMessages=种子消息（banner 之后、spinner 之前）；
    /// commands=斜杠命令清单（宿主注入；null = 无补全能力）；
    /// codeHighlighter=代码高亮插件（M7-5 卫星注入；null = 高亮禁用降级纯文本）；
    /// sizeSource=终端尺寸源（resize 事件化注入面；null = Console 轮询默认实现）；
    /// clipboard=剪贴板写入委托（TASK059 Ctrl+C 复制；null = 复制降级无操作）。</summary>
    public ReplShell(
        IReplEngine engine,
        BannerItem banner,
        TextWriter output,
        bool inline = true,
        IReadOnlyList<IScrollItem>? seedMessages = null,
        IReadOnlyList<CommandSuggestion>? commands = null,
        ICodeHighlighter? codeHighlighter = null,
        ITerminalSizeSource? sizeSource = null,
        Action<string>? clipboard = null)
    {
        _engine = engine;
        _banner = banner;
        _output = output;
        _inline = inline;
        _seedMessages = seedMessages ?? [];
        _suggester = commands is { Count: > 0 } ? new SlashSuggester(commands) : null;
        _sizeSource = sizeSource ?? new ConsoleTerminalSizeSource();
        _copyToClipboard = clipboard;
        _transcript = new TranscriptController(BuildTranscriptSnapshot, verbose => _view.SetVerboseMode(verbose));
        _driver = new ReplBridgeDriver(_view, _spinner, _banner, BeginPermissionRequest, codeHighlighter, _tokenUsage, _sizeSource);
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

    /// <summary>模型选择器协调器（测试断言面）。</summary>
    internal ModelPickerCoordinator ModelPicker => _modelPicker;

    /// <summary>斜杠补全状态机（测试断言面；null = 无命令清单）。</summary>
    internal SlashSuggester? Suggester => _suggester;

    /// <summary>历史反向搜索激活态（Ctrl+R — 测试断言面）。</summary>
    internal bool IsSearching => _search is not null;

    /// <summary>消息光标导航协调器（交互层 Step1 — 测试断言面）。</summary>
    internal MessageCursor CursorNav => _cursorNav;

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
    /// footer 权限模式为占位值，真实引擎由首拍 session_info 事件覆盖；
    /// 动态通知/令牌记账接线（S4 产出消费）：Notifications 队列 + Tokens 记账器挂 footer，
    /// 种子 effort 提示迁移为 Enqueue 一次性入队（保留首屏语义 — 8s 默认时长到期自动清除）。</summary>
    internal void SeedContent()
    {
        _view.Footer = new FooterState
        {
            Mode = PermissionMode.Auto,
            AgentsHint = "← agents",
            LoginError = "未登录 · 运行 /login",
            Notifications = _notifications,
            Tokens = _tokenUsage,
        };
        _notifications.Enqueue($"{Figures.EffortMedium} 中等 · /effort");
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
        var interrupted = false;
        foreach (var bridgeEvent in _engine.DrainEvents())
        {
            if (bridgeEvent is BridgeEvent.TurnInterrupted)
            {
                interrupted = true;
            }
            _driver.Apply(bridgeEvent);
        }

        foreach (var diagnostic in _engine.DrainDiagnostics())
        {
            Console.Error.WriteLine(diagnostic);
        }

        // M8-2 打断恢复（基准 REPL.tsx auto-restore）：打断且无任何有效响应到达 →
        // 恢复用户输入到输入框 + 移除该条用户消息上屏 + 撤销历史条目（等同 rewind 到上一条）。
        // 守卫：输入框为空（不打扰用户已开始的新输入）+ 队列空（队列有独立泵语义）。
        if (interrupted && _input.Text.Length == 0 && _queuedInputs.Count == 0)
        {
            RestoreInterruptedInput();
        }

        if (!_engine.IsAlive && !_driver.IsBusy)
        {
            // 引擎死亡防御 — transcript 模态先退出（全屏模态下退出消息不可见，恢复主屏再上屏）
            if (_transcript.IsActive)
            {
                _transcript.Toggle();
            }

            AddMessage(new ErrorMessageItem("引擎进程已退出，会话结束。"));
            _running = false;
        }

        // 回合结束（完成/打断/异常）时挂起中的权限对话框自动收尾 — 引擎不会再等用户，Deny 关闭避免对话框残留
        if (!_driver.IsBusy && _permission.IsPending)
        {
            CompletePermission(PermissionChoice.Deny);
        }

        // 回合结束 — 排队输入自动发送（TASK060 一问一答节奏：逐回合发队首）
        PumpQueuedInputs();
    }

    /// <summary>打断恢复（M8-2）— 该回合用户消息之后无实质响应项（assistant/工具/流式/错误/子代理）
    /// 时移除该条用户消息 + 恢复文本到输入框 + 历史撤销；有实质响应（含 partial 定格）不恢复。</summary>
    private void RestoreInterruptedInput()
    {
        if (_lastSubmittedIndex < 0 || _lastSubmittedText.Length == 0)
        {
            return;
        }

        for (var i = _lastSubmittedIndex + 1; i < _view.Messages.Count - 1; i++)
        {
            if (_view.Messages.Items[i] is AssistantMessageItem or MarkdownMessageItem or ToolCallItem
                or ToolResultItem or DiffResultItem or ErrorMessageItem or StreamingTextItem
                or SubagentGroupItem or PermissionRequestItem)
            {
                return;
            }
        }

        _view.Messages.RemoveAt(_lastSubmittedIndex);
        FillInput(_lastSubmittedText, cursorToStart: false);
        _history.RemoveLast(_lastSubmittedText);
        _lastSubmittedIndex = -1;
        _lastSubmittedText = string.Empty;
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
        RemoveDialogItem<PermissionRequestItem>();
    }

    /// <summary>
    /// 挂起模型选择器（M7-6 — 宿主装饰器拦截 /model 后调此打开，全链自研不依赖 slash 命令系统）：
    /// 清单由宿主从引擎 IModelCatalog 投影（后端获取）；完成回调参数 = 选中模型 id（Esc 取消为 null）。
    /// title/subtitle 可选参数化（默认模型文案）— vendor 选择器复用同条模态链。
    /// </summary>
    public void BeginModelPicker(IReadOnlyList<ModelPickerItem> items, Action<string?>? onCompleted = null, string title = "Select model", string subtitle = "Switch between models. Applies to this session and future sessions.")
    {
        _modelPicker.Begin(items);
        _modelPickerCallback = onCompleted;
        AddMessage(new ModelPickerDialog(_modelPicker, title, subtitle));
    }

    /// <summary>完成模型选择并移除对话框项（id = null 表示 Esc 取消）。</summary>
    private void CompleteModelPicker(string? id)
    {
        _modelPicker.Complete();
        RemoveDialogItem<ModelPickerDialog>();
        var callback = _modelPickerCallback;
        _modelPickerCallback = null;
        callback?.Invoke(id);
    }

    /// <summary>追加信息反馈消息到滚动区（宿主确认切换后的反馈面 — M7-6 起）。</summary>
    public void PostInfo(string text) => AddMessage(new AssistantMessageItem(text));

    /// <summary>移除滚动区内首个指定类型对话框项（权限/模型对话框共用收尾）。</summary>
    private void RemoveDialogItem<TDialog>()
        where TDialog : class, IScrollItem
    {
        for (var i = 0; i < _view.Messages.Count; i++)
        {
            if (_view.Messages.Items[i] is TDialog)
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

    /// <summary>按键分派 — 权限对话框模态优先（数字/Esc/上下/Enter 路由对话框）→ 模型选择器模态
    /// （M7-6：Esc/上下/Enter 路由选择器）→ 搜索模态（Ctrl+R 反向搜索）→ 消息光标导航模态
    /// （交互层 Step1：光标激活时 ↑↓/kj/Shift 跳转/Enter 展开/Esc 两级退出）→ 补全激活专属键
    /// （Up/Down 导航/Tab 接受/Enter 执行/Esc 关闭，基准 Autocomplete 上下文优先于 Chat）
    /// → transcript 全屏模态（S6 收敛：Controller.IsActive 时整屏独占 — 激活时光标态不可达
    /// （进入被建议后分派拦截），j/k 归 transcript less 键位）
    /// → 帮助面板模态（S6：空闲态 '?' 弹出/Esc 关闭）
    /// ，其余 Emacs 编辑（Ctrl/Meta 词操作 + kill ring），控制键走编辑/提交/权限，字符插入。</summary>
    private void HandleKeyPress(KeyPress key)
    {
        if (_permission.IsPending)
        {
            HandlePermissionKey(key);
            return;
        }

        if (_modelPicker.IsPending)
        {
            HandleModelPickerKey(key);
            return;
        }

        if (_search is not null)
        {
            HandleSearchKey(key);
            return;
        }

        if (_cursorNav.IsCursorActive)
        {
            HandleCursorNavKey(key);
            return;
        }

        if (HandleSuggestionKey(key))
        {
            return;
        }

        if (_transcript.IsActive)
        {
            HandleTranscriptKey(key);
            return;
        }

        if (_helpOpen)
        {
            HandleHelpKey(key);
            return;
        }

        if (IsHelpTrigger(key))
        {
            OpenHelpPanel();
            return;
        }

        if (key is { Kind: KeyKind.UpArrow, Shift: true, Ctrl: false, Meta: false })
        {
            BeginCursorNavigation();
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
                CopyLastAssistantMessage();
                break;
            case "d":
                if (_input.Text.Length == 0)
                {
                    HandleExitCtrlD();
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
            case "l":
                ForceFullRepaint();
                break;
            case "r":
                BeginOrCycleSearch();
                break;
            case "o":
                _transcript.Toggle();
                SyncTranscriptView();
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

    /// <summary>
    /// 模型选择器模态按键（M7-6，对齐基准 ModelPicker Select 交互）：
    /// Esc 取消（回调 null）+ Enter 选焦点项（回调携带 id）+ 上/下 clamp 移动焦点；
    /// 其余按键全部吞掉（模态 — 不进输入缓冲、不提交、不打断）。
    /// </summary>
    private void HandleModelPickerKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Escape)
        {
            CompleteModelPicker(null);
            return;
        }

        if (key.Kind == KeyKind.Return)
        {
            CompleteModelPicker(_modelPicker.Focused?.Id);
            return;
        }

        if (key.Kind == KeyKind.UpArrow)
        {
            _modelPicker.MoveFocus(-1);
            return;
        }

        if (key.Kind == KeyKind.DownArrow)
        {
            _modelPicker.MoveFocus(1);
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

    /// <summary>transcript 模态按键（S6 收敛 — S5 TranscriptController 唯一引擎）：
    /// ctrl+o 分派契约 `IsActive ? (TryHandleKey || toggle) : toggle` — Controller 未消费
    /// （less 键位/搜索/退出之外）且是 ctrl+o 时切换退出（搜索栏打开时 ctrl+o 故意穿透
    /// 供此路径退出）；其余未消费键吞掉（模态 — 字符不进输入框）。
    /// 每键后同步视图投影（滚动/搜索/进出态立即反映 — 渲染帧兜底刷新）。</summary>
    private void HandleTranscriptKey(KeyPress key)
    {
        if (_transcript.TryHandleKey(key))
        {
            SyncTranscriptView();
            return;
        }

        if (key.Ctrl && key is { Kind: KeyKind.Char, Input: "o" })
        {
            _transcript.Toggle();
            SyncTranscriptView();
        }

        // 其余未消费键吞掉（模态 — 修饰组合穿透回宿主仅 ctrl+o 语义）
    }

    /// <summary>'?' 触发判定 — 空闲态（非忙 + 空输入 + 无修饰）的裸 '?'（有输入时 '?' 走正常字符插入；
    /// 光标态/建议态/搜索态在分派链更早拦截不可达此处）。</summary>
    private bool IsHelpTrigger(KeyPress key) =>
        key is { Kind: KeyKind.Char, Input: "?", Ctrl: false, Meta: false, Shift: false }
        && _input.Text.Length == 0
        && !IsBusy;

    /// <summary>打开帮助面板（View 投影同步 — 渲染帧兜底刷新）。</summary>
    private void OpenHelpPanel()
    {
        _helpOpen = true;
        _view.HelpOpen = true;
    }

    /// <summary>帮助面板模态按键 — Esc 或 '?' 关闭（对齐基准 help menu 双关闭键）；
    /// 其余键吞掉（模态 — 不进输入缓冲，面板期间输入暂停）。</summary>
    private void HandleHelpKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Escape || key is { Kind: KeyKind.Char, Input: "?", Ctrl: false, Meta: false })
        {
            _helpOpen = false;
            _view.HelpOpen = false;
        }
    }

    /// <summary>transcript 快照拍摄（Controller 提供器注入面）— 消息全文行投影
    /// （TranscriptLines verbose 形态：CoT 全文/工具输出全量）+ 流式正文长度；
    /// 装饰项（banner/spinner/对话框）投影为空跳过。</summary>
    internal TranscriptSnapshot BuildTranscriptSnapshot()
    {
        var lines = new List<string>();
        var streamingLength = 0;
        foreach (var item in _view.Messages.Items)
        {
            if (item is StreamingTextItem streaming)
            {
                streamingLength = streaming.Text.Length;
            }

            lines.AddRange(TranscriptLines.Project(item));
        }

        return new TranscriptSnapshot(lines, streamingLength);
    }

    /// <summary>transcript 视图投影（渲染前 — RenderAndFlush 每帧调用）：
    /// 视口行数接终端高度（扣除 footer 1 行）+ 行窗口/footer 数据从 Controller 投影到
    /// ReplView.Transcript（非 null 即整屏模态渲染）；未激活置 null 恢复主屏布局。</summary>
    private void SyncTranscriptView()
    {
        if (!_transcript.IsActive)
        {
            _view.Transcript = null;
            return;
        }

        var rows = _sizeSource.Current.Rows;
        _transcript.ViewportHeight = Math.Max(1, rows - TranscriptRenderer.FooterRows);
        _view.Transcript = new TranscriptScreenData(
            _transcript.Snapshot?.Lines ?? [],
            _transcript.ScrollOffset,
            _transcript.ViewportHeight,
            _transcript.BuildFooter());
    }

    /// <summary>进入或循环搜索（Ctrl+R）— 未激活：以当前输入为查询进入（暂存草稿）；
    /// 已激活由 HandleSearchKey 拦截（本分支不会命中，防御保留）。</summary>
    private void BeginOrCycleSearch()
    {
        if (_search is not null)
        {
            _search.Next();
            return;
        }

        _preSearchDraft = _input.Text;
        _search = HistorySearch.Begin(_input.Text, _history.Entries);
    }

    /// <summary>搜索态按键模态（基准 ctrl+r 反向搜索交互）：
    /// Ctrl+R 循环下一个匹配；Enter 接受当前匹配（填入输入并提交，无匹配 no-op）；
    /// Esc 退出恢复草稿；Up/Down 匹配间导航；字符/退格编辑查询（刷新匹配、重置最新）；
    /// 其余键吞掉（模态 — 查询编辑设程序化标记防触发斜杠补全）。</summary>
    private void HandleSearchKey(KeyPress key)
    {
        var search = _search;
        if (search is null)
        {
            return;
        }

        if (key.Ctrl && key is { Kind: KeyKind.Char, Input: "r" })
        {
            search.Next();
            return;
        }

        switch (key.Kind)
        {
            case KeyKind.Return:
                if (search.Current.Length > 0)
                {
                    var accepted = search.Current;
                    _search = null;
                    FillInput(accepted, cursorToStart: false);
                    Submit();
                }

                return;
            case KeyKind.Escape:
                var draft = _preSearchDraft;
                _search = null;
                FillInput(draft, cursorToStart: false);
                return;
            case KeyKind.UpArrow:
                search.Previous();
                return;
            case KeyKind.DownArrow:
                search.Next();
                return;
            case KeyKind.Backspace:
                _input.Backspace();
                search.UpdateQuery(_input.Text);
                _programmaticFill = true;
                return;
            case KeyKind.Char when !key.Ctrl && !key.Meta && key.Input.Length == 1:
                _input.Insert(key.Input[0]);
                search.UpdateQuery(_input.Text);
                _programmaticFill = true;
                return;
            default:
                // 模态：其余键吞掉（Home/End/Delete 等编辑能力后续按需开放）
                return;
        }
    }

    /// <summary>底部操作条渲染项（M8-6 消息操作复制 — 导航态激活挂载于 spinner 之前 / 退出卸载）。</summary>

    /// <summary>
    /// 消息光标导航模态按键（交互层 Step1，对齐基准 MESSAGE_ACTIONS 上下文键绑定）：
    /// ↑/↓/k/j 相邻可导航消息逐条移动、Shift+↑/↓ 跳相邻用户消息、Ctrl+↑/↓ 首末可导航消息、
    /// Enter 切换选中分组展开态（基准展开 grouped_tool_use → verbose 逐代理详细渲染）、
    /// c 复制选中消息全文 / p 复制工具主参数（M8-6 — 复制后不退出导航，基准 copy 键位）、
    /// Esc 两级退出（任一分组展开先全部收起、再退出导航）；
    /// 其余按键全部吞掉（模态 — 基准全屏模式 PromptInput 卸载语义，不进输入缓冲）。
    /// </summary>
    private void HandleCursorNavKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Return)
        {
            ToggleSelectedGroupExpansion();
            return;
        }

        if (key.Kind == KeyKind.Escape)
        {
            EscapeCursorNavigation();
            return;
        }

        var items = _view.Messages.Items;
        if (key is { Kind: KeyKind.UpArrow, Shift: true, Ctrl: false, Meta: false })
        {
            _cursorNav.MoveToUserMessage(items, -1);
            return;
        }

        if (key is { Kind: KeyKind.DownArrow, Shift: true, Ctrl: false, Meta: false })
        {
            _cursorNav.MoveToUserMessage(items, 1);
            return;
        }

        if (key is { Kind: KeyKind.UpArrow, Ctrl: true, Shift: false, Meta: false })
        {
            _cursorNav.MoveToEdge(items, first: true);
            return;
        }

        if (key is { Kind: KeyKind.DownArrow, Ctrl: true, Shift: false, Meta: false })
        {
            _cursorNav.MoveToEdge(items, first: false);
            return;
        }

        switch (key.Kind)
        {
            case KeyKind.UpArrow:
                _cursorNav.Move(items, -1);
                return;
            case KeyKind.DownArrow:
                _cursorNav.Move(items, 1);
                return;
            case KeyKind.Char when !key.Ctrl && !key.Meta && key.Input is "k":
                _cursorNav.Move(items, -1);
                return;
            case KeyKind.Char when !key.Ctrl && !key.Meta && key.Input is "j":
                _cursorNav.Move(items, 1);
                return;
            case KeyKind.Char when !key.Ctrl && !key.Meta && key.Input is "c":
                CopySelectedFullText();
                return;
            case KeyKind.Char when !key.Ctrl && !key.Meta && key.Input is "p":
                CopySelectedPrimaryInput();
                return;
            default:
                // 模态：其余键吞掉（不进输入缓冲 — 基准光标态 PromptInput 卸载）
                return;
        }
    }

    /// <summary>光标态 c — 选中项全文复制（MessageCursor.ExtractFullText → 剪贴板委托 +
    /// 通知队列反馈"已复制全文"）；不可复制类型/未注入剪贴板静默无操作。</summary>
    private void CopySelectedFullText() =>
        CopyToClipboard(_cursorNav.ExtractFullText(_view.Messages.Items), "已复制全文");

    /// <summary>光标态 p — 选中工具调用主参数复制（MessageCursor.ExtractPrimaryInput → 剪贴板委托 +
    /// 通知队列反馈"已复制主参数"）；非工具调用项/映射外工具/未注入剪贴板静默无操作。</summary>
    private void CopySelectedPrimaryInput() =>
        CopyToClipboard(_cursorNav.ExtractPrimaryInput(_view.Messages.Items), "已复制主参数");

    /// <summary>剪贴板写入 + 通知反馈（文本 null = 无可复制面静默；未注入剪贴板委托静默 — 降级无操作）。</summary>
    private void CopyToClipboard(string? text, string feedback)
    {
        if (text is null || _copyToClipboard is null)
        {
            return;
        }

        _copyToClipboard(text);
        _notifications.Enqueue(feedback);
    }

    /// <summary>进入消息光标导航（Shift+↑ — 定位最后一条用户消息；无可导航消息 no-op）。</summary>
    private void BeginCursorNavigation() => _cursorNav.Begin(_view.Messages.Items);

    /// <summary>Enter 切换选中分组展开态（选中非分组消息 no-op — 基准仅 grouped_tool_use 可展开）。</summary>
    private void ToggleSelectedGroupExpansion()
    {
        if (_cursorNav.SelectedIndex is >= 0 and var index
            && index < _view.Messages.Count
            && _view.Messages.Items[index] is SubagentGroupItem group)
        {
            group.Coordinator.IsExpanded = !group.Coordinator.IsExpanded;
        }
    }

    /// <summary>Esc 两级退出 — 任一分组展开先全部收起（导航保持、操作条保持）；
    /// 否则退出光标导航并卸载底部操作条。</summary>
    private void EscapeCursorNavigation()
    {
        var expandedGroups = _view.Messages.Items
            .OfType<SubagentGroupItem>()
            .Where(static group => group.Coordinator.IsExpanded)
            .ToList();
        if (expandedGroups.Count > 0)
        {
            foreach (var group in expandedGroups)
            {
                group.Coordinator.IsExpanded = false;
            }

            return;
        }

        _cursorNav.Exit();
    }

    /// <summary>补全激活时的专属键拦截（基准 Autocomplete 上下文）：
    /// 下拉激活：Up/Down 循环导航、Tab 接受不执行、Enter 接受并提交、Esc 仅关建议（优先于打断/清输入/退出）；
    /// ghost 激活（下拉互斥为空）：RightArrow（光标在末尾）接受幽灵补全、Esc 仅关 ghost；
    /// 其余键返回 false 走正常编辑路径（编辑后刷新建议）。</summary>
    private bool HandleSuggestionKey(KeyPress key)
    {
        if (_suggester is not { } suggester)
        {
            return false;
        }

        if (suggester.IsActive)
        {
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

        if (suggester.GhostSuffix.Length > 0)
        {
            if (key.Kind == KeyKind.RightArrow && _input.CursorCol == _input.Text.Length)
            {
                FillInput(_input.Text + suggester.GhostSuffix, cursorToStart: false);
                return true;
            }

            if (key.Kind == KeyKind.Escape)
            {
                suggester.Dismiss();
                return true;
            }
        }

        return false;
    }

    /// <summary>接受选中建议（基准 applyCommandSuggestion）— 命令模式：Tab 补全 "/命令 "（尾随空格进参数态）/
    /// Enter 直接提交 "/命令"；参数模式（IsArgumentMode）：真实值清单时补全 "/命令 参数 " 或提交 "/命令 参数"，
    /// 仅占位提示（HasRealArgumentValues=false，如 "&lt;question&gt;"）时 Tab 不填入、Enter 提交输入原文
    ///（修复：占位符被当参数值填入提交，`/btw &lt;question&gt;` 字面量发给了 AI）。</summary>
    private void AcceptSuggestion(bool execute)
    {
        if (_suggester?.Selected is not { } selected)
        {
            return;
        }

        if (_suggester.IsArgumentMode && !_suggester.HasRealArgumentValues)
        {
            if (execute)
            {
                Submit();
            }

            return;
        }

        string completed;
        if (_suggester.IsArgumentMode)
        {
            var spaceIndex = _input.Text.IndexOf(' ');
            if (spaceIndex <= 1)
            {
                return;
            }

            var commandName = _input.Text[1..spaceIndex];
            completed = $"/{commandName} {selected.Name}";
        }
        else
        {
            completed = $"/{selected.Name}";
        }

        FillInput(execute ? completed : completed + " ", cursorToStart: false);
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

    /// <summary>Esc 处置（TASK059 退出安全 — Esc 永不退出；M8-2 打断闭环补全基准重载链）：
    /// 忙时：有排队输入 → 单击弹回全部到输入框（基准 popAllCommandsFromQueue — 优先于打断判定）；
    /// 无排队 → 500ms 双击打断（单击等窗口无操作）。
    /// 闲时：有文本 → 清空输入；空输入 → 无操作。退出路径只剩 Ctrl+D 与 /exit 命令。</summary>
    internal void HandleEscape()
    {
        var now = EscClock();
        var isDoubleTap = _lastEscapeMs != 0 && now - _lastEscapeMs <= EscapeDoubleClickWindowMs;
        _lastEscapeMs = now;

        if (IsBusy)
        {
            if (_queuedInputs.Count > 0)
            {
                // M8-2 基准重载链⑤ — 可编辑排队命令全部弹回输入框（按行拼接）+ 清空队列 + 重置双击窗口
                var merged = string.Join("\n", _queuedInputs);
                _queuedInputs.Clear();
                _view.QueuedInputs = _queuedInputs;
                FillInput(merged, cursorToStart: false);
                _lastEscapeMs = 0;
                return;
            }

            if (isDoubleTap)
            {
                _lastEscapeMs = 0;
                _engine.Interrupt();
            }

            return;
        }

        if (_input.Text.Length > 0)
        {
            _input.Clear();
        }
    }

    /// <summary>Ctrl+C 复制（TASK059 — 原退出行为移除）：滚动区最后一条 assistant 消息文本 →
    /// 剪贴板委托（宿主注入；null = 降级无操作）+ PostInfo 反馈；无 assistant 消息时无操作。</summary>
    internal void CopyLastAssistantMessage()
    {
        if (_copyToClipboard is null)
        {
            return;
        }

        var last = _view.Messages.Items.OfType<AssistantMessageItem>().LastOrDefault();
        if (last is null)
        {
            return;
        }

        _copyToClipboard(last.Text);
        PostInfo("已复制到剪贴板");
    }

    /// <summary>Ctrl+O 切换最近一条消息的思考块展开/收起（TASK062 结构化消息 — 基准单条聚焦语义）：
    /// 找最后一条带 CoT 的消息 Toggle（Assistant/Markdown 两类取滚动区更靠后者）；
    /// 无 CoT 无操作（静默 — 非思考模型路径）；高度变化由下次 MeasureHeight 反映（渲染循环自动重排）。</summary>
    internal void ToggleLastThinking()
    {
        var items = _view.Messages.Items;
        var markdownIndex = IndexOfItem(items, items.OfType<MarkdownMessageItem>().LastOrDefault(m => m.ThinkingContent is { Length: > 0 }));
        var plainIndex = IndexOfItem(items, items.OfType<AssistantMessageItem>().LastOrDefault(m => m.ThinkingContent is { Length: > 0 }));
        if (markdownIndex < 0 && plainIndex < 0)
        {
            return;
        }

        if (markdownIndex > plainIndex)
        {
            ((MarkdownMessageItem)items[markdownIndex]).ToggleThinking();
        }
        else
        {
            ((AssistantMessageItem)items[plainIndex]).ToggleThinking();
        }
    }

    /// <summary>滚动区项索引（引用相等查找；null = -1）。</summary>
    private static int IndexOfItem(IReadOnlyList<IScrollItem> items, IScrollItem? target) {
        if (target is null)
            return -1;
        for (var i = items.Count - 1; i >= 0; i--) {
            if (ReferenceEquals(items[i], target))
                return i;
        }
        return -1;
    }

    /// <summary>Shift+Tab 循环权限模式（jcc 引擎模式集合）— 发引擎应用请求，footer 以 permission_mode_changed 回执为准。</summary>
    internal void CyclePermissionMode()
    {
        var next = PermissionModeCycler.Next(_view.Footer.Mode, _driver.BypassAvailable);
        _engine.SetPermissionMode(PermissionModeInfo.ToValue(next));
    }

    /// <summary>提交输入（TASK060 一问一答节奏 + TASK062 后续排队面板）：空文本忽略；未登录本地反馈
    /// （基准 ⎿ Not logged in — 不调引擎不傻等）；忙时改排队（基准 queued 语义）— **不上滚动区**
    ///（输入栏上方 queued 面板渲染 — TASK062 用户验收反馈：滚动区排队提示破坏消息流），入历史环，
    /// 回合结束自动按序发送（DrainEngine 队列泵 — 发送时才上屏用户消息）；闲时正常直发。</summary>
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
        if (!LoginGateOpen)
        {
            AddMessage(new UserMessageItem(text));
            AddMessage(new ToolResultItem("未登录 · 请在 auth.json 配置有效 API key 或运行 /login 后重试"));
            return;
        }

        if (IsBusy)
        {
            if (_queuedInputs.Count >= MaxQueuedInputs)
            {
                AddMessage(new ToolResultItem($"排队已满（{MaxQueuedInputs} 条上限）— 本条丢弃，请等当前回合结束"));
                _view.QueuedInputs = _queuedInputs;
                return;
            }

            _queuedInputs.Add(text);
            _view.QueuedInputs = _queuedInputs;
            return;
        }

        AddMessage(new UserMessageItem(text));
        _lastSubmittedText = text;
        _lastSubmittedIndex = _view.Messages.Count - 2;
        _engine.Submit(text);
    }

    /// <summary>排队输入发送泵（DrainEngine 回合结束时调用）— 非忙态且有排队时取队首：
    /// 上屏用户消息（发送时才进滚动区 — 排队期间只在输入栏上方面板）+ 直发引擎；
    /// 逐回合发一条维持一问一答节奏。</summary>
    private void PumpQueuedInputs()
    {
        if (IsBusy || _queuedInputs.Count == 0)
        {
            return;
        }

        var text = _queuedInputs[0];
        _queuedInputs.RemoveAt(0);
        AddMessage(new UserMessageItem(text));
        _view.QueuedInputs = _queuedInputs;
        _engine.Submit(text);
    }

    /// <summary>渲染一帧并差分上屏：inline 走相对光标 + append（历史滚入 scrollback）+ DEC 2026 原子上屏；视口走绝对定位 diff。
    /// internal = resize/渲染测试的单帧驱动面。transcript 模态：视口投影 + 帧拍 Tick（S6 接线）+ 帧尾光标走搜索栏位。</summary>
    internal void RenderAndFlush()
    {
        if (_screenClearPending)
        {
            _screenClearPending = false;
            // 终端层清屏 + 清 scrollback（对话历史滚入区）+ 光标回原点；渲染基线重置 — 下一帧按首帧语义重绘 banner+spinner+输入区
            _output.Write(Sgr.Reset + Csi.EraseScreen() + Csi.EraseSavedLines() + Csi.CursorPosition(1, 1));
            _output.Flush();
            _frontValid = false;
            _prevOverride = null;
            _cursor = new CursorPos(Col: 0, Row: 0);
        }

        SyncSuggestionState();
        SyncCursorHighlight();
        SyncTranscriptView();
        _view.HelpOpen = _helpOpen;
        EnsureScreens();
        _back.Clear();
        AdvanceAnimClock();
        _transcript.Tick(TickMs);
        _view.Footer.Notifications?.Tick(_animTimeMs);
        _view.Input = _input.Text;
        _view.CursorCol = _input.CursorCol;
        _view.Render(_back, _theme);
        if (_inline)
        {
            var viewportRows = _sizeSource.Current.Rows;
            var frameEnd = _view.Transcript is { } transcript
                ? TranscriptRenderer.CursorPos(transcript, _back.Rows)
                : _view.InputCursorPos(_back.Rows, _back.Cols, _input.CursorCol);
            var prev = _frontValid ? _front : _prevOverride;
            if (_forceFullReset)
            {
                _forceFullReset = false;
                _blankPrev ??= new Screen(0, 0, sharedStyles: _pool);
                prev = _blankPrev;
            }

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

    /// <summary>动画时钟推进 + 广播：每拍 +50ms，喂给滚动区全部 IAnimatable（基准 useBlink 共享时钟 — 全实例同步闪烁）；
    /// spinner 专属墙钟一并注入（elapsed 暂停感知，语义区别于动画相位时钟）。</summary>
    internal void AdvanceAnimClock()
    {
        _animTimeMs += TickMs;
        foreach (var item in _view.Messages.Items)
        {
            if (item is IAnimatable animatable)
            {
                animatable.AnimTimeMs = _animTimeMs;
            }
        }

        _spinner.WallNowMs = Environment.TickCount64;
    }

    /// <summary>后缓冲（resize 测试断言面 — 屏尺寸/重建复用判定）。</summary>
    internal Screen? BackBuffer => _back;

    /// <summary>前缓冲（渲染完成帧 — 测试断言面：RenderAndFlush 写入 _back 后交换至此）。</summary>
    internal Screen? FrontBuffer => _front;

    /// <summary>resize 跨尺寸 diff 旧帧（测试断言面 — 重建时保留旧屏供 InlineComposer append/fullReset）。</summary>
    internal Screen? PrevOverride => _prevOverride;

    /// <summary>同步补全建议到视图（渲染前状态投影 — EnsureScreens 屏高计算依赖建议区高度，必须先行）。</summary>
    private void SyncSuggestionState()
    {
        _view.Suggestions = _suggester is { IsActive: true } ? _suggester.Items : null;
        _view.SuggestionIndex = _suggester?.SelectedIndex ?? 0;
        _view.SuggestionNameColumnWidth = _suggester?.NameColumnWidth ?? 0;
        _view.SuggestionArgumentMode = _suggester?.IsArgumentMode ?? false;
        _view.GhostSuffix = _search is { Current.Length: > 0 } ? _search.Current : _suggester?.GhostSuffix ?? string.Empty;
        _view.QueuedInputs = _queuedInputs;
    }

    /// <summary>同步消息光标选中态到视图（渲染前状态投影）：
    /// 高亮项（ScrollBox 渲染后整块背景覆写）+ 消息操作条（MessageActionsState.Of(选中项) —
    /// null = 光标态未激活不渲染；键位提示条随选中项动态派生 c 复制/p 主参数段）。</summary>
    private void SyncCursorHighlight()
    {
        var selected = _cursorNav.IsCursorActive
            && _cursorNav.SelectedIndex is >= 0 and var index
            && index < _view.Messages.Count
                ? _view.Messages.Items[index]
                : null;
        _view.Messages.HighlightItem = selected;
        _view.MessageActions = selected is not null ? MessageActionsState.Of(selected) : null;
    }

    /// <summary>
    /// 尺寸计算（含首帧）成对重建双缓冲 — 尺寸取自注入源（resize 事件化，不读 Console 静态环境）；
    /// inline 屏高 = 内容总高（消息全量展示，可超视口 — 终端 rows 非 resize 敏感维度），
    /// 重建时保留旧帧跨尺寸 diff（增长 append / 宽度变 fullReset）；视口模式屏高 = 尺寸源行数；
    /// transcript 模态屏高 = 终端行数（整屏模态 — 进出 transcript 行数变化触发重建跨尺寸 diff）。
    /// </summary>
    internal void EnsureScreens()
    {
        var (cols, rows) = _sizeSource.Current;
        var backRows = _inline
            ? (_transcript.IsActive ? rows : Math.Max(8, _view.MeasureContentHeight(cols)))
            : rows;
        if (_back is not null && cols == _back.Cols && backRows == _back.Rows)
        {
            return;
        }

        _prevOverride = _inline && _frontValid ? _front : null;
        _front = new Screen(cols, backRows, sharedStyles: _pool);
        _back = new Screen(cols, backRows, sharedStyles: _pool);
        _frontValid = false;
    }

    /// <summary>退出输出 — inline：清尾部动态区（间隔+输入区+footer），历史留终端 scrollback 可复制；视口：清屏复位；
    /// transcript 残留防御（引擎死亡路径已先退出，此处兜底）：清屏复位（transcript 态无 scrollback 语义可保留）。</summary>
    public void WriteExit()
    {
        if (!_inline || _view.Transcript is not null)
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

    /// <summary>清滚动区（M8-5 — /clear 接线，引擎回合线程回调）：
    /// 移除 banner 与常驻 spinner 之间全部消息项（保留 SeedContent 不变式）+ 置清屏待决标记 —
    /// 终端层 2J+3J 清屏延到主循环 RenderAndFlush 消费（回调线程不写终端 — 与渲染循环并发写交错）。</summary>
    public void ClearMessagesExceptBannerAndSpinner()
    {
        for (var i = _view.Messages.Count - 2; i > 0; i--)
        {
            _view.Messages.RemoveAt(i);
        }

        _view.Messages.ScrollToBottom();
        _screenClearPending = true;
    }

    /// <summary>请求退出主循环（M8-5 + TASK065 F1 — /exit /quit /logout 命令退出链）：
    /// 上屏命令退出提示（dim ⎿ 行，reason 为命令名）+ 置 _running=false 终止主循环
    ///（对齐 CLI ShouldContinue=false→Stop；Run() 当前拍收尾返回后由宿主 WriteExit 收尾）。
    /// 无参重载兼容 Action 接线形态。</summary>
    public void RequestExit() => RequestExit("exit");

    /// <summary>请求退出主循环（带命令名 — 退出提示行显示 /{reason}）。</summary>
    public void RequestExit(string reason)
    {
        if (!_running)
        {
            return;
        }

        AddMessage(new ToolResultItem($"/{reason} — 退出 jcc"));
        _running = false;
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

    /// <summary>ctrl+l 强制全量重绘（S6 任务 8）：inline — 下一帧走 FullReset（零尺寸空白前置帧
    /// 触发 InlineComposer 跨尺寸分支：光标移视口顶 + EraseScreen + 重绘可见区，scrollback 历史保留）；
    /// viewport — 前缓冲失效下帧全量输出。prevOverride 一并丢弃（旧帧不复用作 diff 基准）。</summary>
    internal void ForceFullRepaint()
    {
        _frontValid = false;
        _prevOverride = null;
        _forceFullReset = true;
    }
}
