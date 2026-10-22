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

    /// <summary>当前渲染主题（S9 接线 — ThemeSelection.Resolve(配置名) 解析，默认 dark；
    /// /theme 拦截切换后下一帧生效，构造 themeName 由宿主配置通道注入）。</summary>
    private Theme _theme = ThemeSelection.Resolve(null);
    private readonly ReplView _view = new();
    private readonly InputBuffer _input = new();
    private readonly SpinnerItem _spinner = new() { Message = string.Empty, Hidden = true };
    private readonly StylePool _pool = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly InlineComposer _inlineComposer = new();

    /// <summary>视口模式帧组装器（S9 接线 — DEC 2026 探测结果条件构造：不支持终端跳过 BSU/ESU 包裹）。</summary>
    private readonly FrameComposer _composer = new(TerminalCapabilities.IsSynchronizedOutputSupported());

    /// <summary>inline 模式 DEC 2026 同步输出支持（S9 接线 — 直拼 SynchronizedOutputBegin/End 的同一条件，
    /// 构造时探测一次会话级缓存）。</summary>
    private readonly bool _syncOutputSupported = TerminalCapabilities.IsSynchronizedOutputSupported();

    /// <summary>引擎交互面（提交/打断/权限/事件排空/存活）。</summary>
    private readonly IReplEngine _engine;

    /// <summary>渲染 writer（宿主注入 — Host 为重定向前捕获的独立 writer）。</summary>
    private readonly TextWriter _output;

    /// <summary>bridge 事件应用器（引擎事件 → 视图状态唯一翻译层）。</summary>
    private readonly ReplBridgeDriver _driver;

    /// <summary>权限对话框协调器（M7-3 — 挂起时键盘模态改道）。</summary>
    private readonly PermissionCoordinator _permission = new();

    /// <summary>命令交互对话框协调器（TASK065 F2 — 斜杠命令 confirm/prompt 挂起时键盘模态改道，
    /// 优先级低于权限对话框）。</summary>
    private readonly CommandDialogCoordinator _commandDialog = new();

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

    /// <summary>忙时排队输入队列（TASK060 — 基准 queued 语义，回合结束逐条自动发送）。
    /// S7 提交全文：元组 = 显示形态（折叠 [Pasted text #N]）+ 提交形态（粘贴引用展开全文）—
    /// 提交时刻完成展开（InputBuffer.Clear 清空登记簿后不可再展开）。</summary>
    private readonly List<(string Display, string Expanded)> _queuedInputs = [];

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

    /// <summary>ctrl+s 输入暂存单槽（TASK064 P3 — 基准 defaultBindings.ts:85 chat:stash +
    /// PromptInputStashNotice.tsx:18 单槽语义）：覆盖式收起当前输入腾出输入框（忙/闲均可用），
    /// 下次 Submit 提交动作完成后自动回填输入框；空输入无操作。</summary>
    private string _stashedInput = string.Empty;

    /// <summary>暂存单槽内容（internal — 测试断言面；空串 = 无暂存）。</summary>
    internal string StashedInput => _stashedInput;

    /// <summary>外部编辑器会话进行中（internal — 测试断言面）。</summary>
    internal bool ExternalEditorActive => _externalEditorActive;

    /// <summary>导出对话框挂载态（S12 — internal 测试断言面）。</summary>
    internal bool IsExportDialogOpen => _exportDialog is not null;

    /// <summary>快速打开对话框挂载态（S12 — internal 测试断言面）。</summary>
    internal bool IsQuickOpenOpen => _quickOpenDialog is not null;

    /// <summary>全局搜索对话框挂载态（S12 — internal 测试断言面）。</summary>
    internal bool IsGlobalSearchOpen => _globalSearchDialog is not null;

    /// <summary>全局搜索对话框实例（S12 — internal 测试断言面：查询/结果/选中状态机）。</summary>
    internal GlobalSearchDialog? GlobalSearchDialog => _globalSearchDialog;

    /// <summary>外部编辑器进行中任务句柄（internal — 测试 await 完成面；null = 无进行中会话）。</summary>
    internal Task? ExternalEditorTask => _externalEditorTask;

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

    /// <summary>命令使用记忆（S10 接线 — 斜杠命令提交时 RecordUse（提交即使用：TUI 无引擎侧执行成败回执，
    /// recently-used 分组语义 = 用户常用命令）；SlashSuggester 空查询分组投影数据源；退出 fire-and-forget 持久化）。</summary>
    private readonly CommandUsageMemory _commandUsage = new();

    /// <summary>bash 直通执行器（S10 接线 — `!` 前缀命令；Windows 宿主 cmd.exe /c，其余 bash -c）。</summary>
    private readonly BashDirectExecutor _bash = OperatingSystem.IsWindows()
        ? new BashDirectExecutor("cmd.exe", "/c")
        : new BashDirectExecutor();

    /// <summary>bash 直通完成结果缓冲（S10 接线 — ExecuteAsync 完成于线程池线程，AddMessage 必须回主循环线程
    /// （与渲染循环竞争 _view.Messages），复用 /clear 的跨线程缓冲 + 主循环消费先例）。</summary>
    private readonly ConcurrentQueue<BashDirectResult> _bashResults = new();

    /// <summary>bash 直通超时守卫（S10 接线 — 执行命令允许超时：30s 杀进程树返回部分输出 + Interrupted）。</summary>
    private static readonly TimeSpan BashDirectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>@ 文件补全数据层（S10 接线 — 构造注入工作区根；null = 禁用降级无建议）。</summary>
    private readonly AtFileSuggester? _atFileSuggester;

    /// <summary>@ 文件补全当前建议投影（CommandSuggestion 形态喂 SuggestionBox 渲染 — label=相对路径；
    /// null = 未激活。选中索引复用 _suggester 通道变量 _atSuggestionIndex）。</summary>
    private IReadOnlyList<CommandSuggestion>? _atSuggestions;

    /// <summary>@ 文件补全选中索引（SuggestionBox 导航）。</summary>
    private int _atSuggestionIndex;

    /// <summary>@ 文件补全抑制 token（Esc 关闭后同一 token 不再建议 — 对齐 SlashSuggester.Dismiss 抑制语义）。</summary>
    private string _atSuppressedToken = string.Empty;

    /// <summary>动态通知队列（S4 产出 — footer 行 2 右侧渲染源；帧拍 Tick 推进到期/出队，
    /// 种子 effort 提示经 Enqueue 一次性入队保留首屏语义）。</summary>
    private readonly ReplNotifications _notifications = new();

    /// <summary>键盘输入泵（DSG042 — 通道抽象：ReadKeyPump 现役 / RawStdinPump 原始流灰度）。</summary>
    private readonly IInputPump _inputPump;

    /// <summary>剪贴板写入委托（TASK059 — Ctrl+C 复制最后 assistant 消息；宿主注入引擎
    /// IClipboardService 适配，Core 零依赖不持接口；null = 复制能力降级无操作）。</summary>
    private readonly Action<string>? _copyToClipboard;

    /// <summary>主题持久化委托（S11 遗留#1 — /theme 合法切换后宿主写回引擎 settings.json；
    /// 宿主注入 IConfigurationService.SetAsync 适配，Core 零依赖；null = 持久化降级会话级生效）。</summary>
    private readonly Func<string, Task>? _saveThemeAsync;

    /// <summary>后台任务停止委托（M8-8 后续 — 空闲态 x 停止面板运行中任务，参数 = (kind, taskId)；
    /// 宿主注入引擎停止转发（进程内 EngineTurnClient / bridge BridgeEngine — IReplEngine 多态统一面），
    /// Core 零依赖不持接口（对齐 clipboard 注入先例）；null = 停止能力降级，x 走正常字符插入）。</summary>
    private readonly Action<string, string>? _onStopTask;

    /// <summary>后台任务详情对话框开（M8-8 登记遗留收口 — 选中态 Enter 挂载，Esc/left 关闭；
    /// 对齐 _helpOpen 轻量模态先例 — 对话框快照构造无协调器，挂载态由本标记承载分派路由；
    /// 激活期间分派链插入选中态之前吞全部按键，关闭后回选中态继续导航）。</summary>
    private bool _backgroundTaskDetailOpen;

    /// <summary>后台任务详情对话框态（internal — 测试断言面）。</summary>
    internal bool IsBackgroundTaskDetailOpen => _backgroundTaskDetailOpen;

    /// <summary>外部编辑器启动委托（TASK064 P3 — 基准 defaultBindings.ts:83-84 ctrl+g/ctrl+x ctrl+e →
    /// chat:externalEditor → $EDITOR）：输入文本 → 编辑后文本；null = 编辑器失败/取消降级丢弃。
    /// Core 零依赖不启动进程 — 宿主注入（对齐 clipboard/onStopTask 注入先例）。</summary>
    private readonly Func<string, Task<string?>>? _launchExternalEditor;

    /// <summary>外部编辑器会话进行中（防重入 — 编辑器未收尾前再按 ctrl+g 无操作，不并发开两个编辑器）。</summary>
    private bool _externalEditorActive;

    /// <summary>外部编辑器进行中任务句柄（fire-and-forget 保留引用 — internal 测试 await 面）。</summary>
    private Task? _externalEditorTask;

    /// <summary>外部编辑器完成结果缓冲（TASK064 P3 — 完成于线程池线程，FillInput 必须回主循环线程
    ///（与渲染循环竞争 _input），复用 bash 直通跨线程缓冲 + DrainEngine 主循环消费先例）：
    /// 元组 = (发起时原文, 编辑后文本 — null = 编辑器失败/取消丢弃信号)。</summary>
    private readonly ConcurrentQueue<(string Original, string? Edited)> _externalEditorResults = new();

    /// <summary>ctrl+x 前缀序列标记（M2 — 基准 defaultBindings.ts:83-84 ctrl+x ctrl+e →
    /// chat:externalEditor 第二键位）：ctrl+x 置位，紧随其后的 ctrl+e 消费触发外部编辑器；
    /// HandleCtrlKey 入口捕获复位 — 任何其他 ctrl 键到达即作废前缀态（无超时简化：基准无超时，
    /// 陈旧前缀由下一次 ctrl 键到达自然清除；ctrl+x 自身重新置位延续等待 — readline 前缀语义，
    /// 可扩展承载后续 ctrl+x ctrl+k 等序列，本批次仅接编辑器不预埋）。</summary>
    private bool _ctrlXPrefix;

    /// <summary>导出对话对话框挂载态（S12 四域接线 — /export 命令拦截挂载 ExportDialog 模态；
    /// null = 未挂载；互斥由分派链序保证 — 模态激活期间其余对话框触发键被吞）。</summary>
    private ExportDialog? _exportDialog;

    /// <summary>导出快照文本（S12 — 挂载时 TranscriptExporter.Export 一次性拍平；
    /// string 不可变 = 防御拷贝，挂载后滚动区变化不影响导出内容）。</summary>
    private string _pendingExportText = string.Empty;

    /// <summary>导出文件写委托（S12 — ExportDialog 确认 File 目标后写盘；参数 = 导出全文，
    /// 返回写入文件名（null = 失败）；Core 零依赖不触 IO — 宿主注入，对齐 clipboard 注入先例）。</summary>
    private readonly Func<string, Task<string?>>? _saveExportAsync;

    /// <summary>导出文件写完成结果缓冲（S12 — 完成于线程池线程，通知反馈必须回主循环线程
    ///（与渲染循环竞争 _notifications），复用 bash 直通跨线程缓冲 + DrainEngine 主循环消费先例）：
    /// null = 写盘失败信号。</summary>
    private readonly ConcurrentQueue<string?> _exportFileResults = new();

    /// <summary>快速打开对话框挂载态（S12 四域接线 — ctrl+shift+p 触发挂载；
    /// null = 未挂载；互斥由分派链序保证）。</summary>
    private QuickOpenDialog? _quickOpenDialog;

    /// <summary>全局搜索对话框挂载态（S12 四域接线 — ctrl+shift+f 触发挂载；
    /// null = 未挂载；互斥由分派链序保证）。</summary>
    private GlobalSearchDialog? _globalSearchDialog;

    /// <summary>全局搜索根目录（S12 — 构造 workspaceRoot 注入留存；对话框构造透传搜索源）。</summary>
    private readonly string? _workspaceRoot;

    /// <summary>全局搜索源（S12 四域接线 — 宿主注入 rg 执行器/测试注入假源；null = 无后端降级）。</summary>
    private readonly ISearchSource? _searchSource;

    /// <summary>全局搜索防抖待派发时间戳（S12 — 查询变化记帧拍时间戳，Tick 到期派发；
    /// 0 = 无待派发。禁 Task.Delay — 对齐 ToolGroupAggregator.OnFrame 帧拍时间戳先例）。</summary>
    private long _globalSearchPendingSinceMs;

    /// <summary>全局搜索已派发查询（防抖幂等 — 查询与已派发一致时不重复派发）。</summary>
    private string _globalSearchDispatchedQuery = string.Empty;

    /// <summary>全局搜索进行中令牌源（S12 — 组件不持运行中任务契约：宿主持有，
    /// 重入派发先 Cancel 旧令牌（取消为常态 — 组件保留已收集部分静默收尾），关闭时终结）。</summary>
    private CancellationTokenSource? _globalSearchCts;

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
    /// clipboard=剪贴板写入委托（TASK059 Ctrl+C 复制；null = 复制降级无操作）；
    /// themeName=主题配置名（S9 接线 — ThemeSelection.Resolve 解析，null/未知名回退 dark）；
    /// commandUsage=命令使用记忆（S10 接线 — 宿主启动加载持久化数据注入；null = 空记忆）；
    /// workspaceRoot=工作区根目录（S10 接线 — @ 文件补全索引根；null = @ 补全禁用降级）；
    /// onStopTask=后台任务停止委托（M8-8 后续 — 空闲态 x 停止面板运行中任务，参数 = (kind, taskId)；
    /// null = 停止能力降级，x 走正常字符插入）；
    /// streamingProjector=流式 Markdown 块级增量投影器（方案 B 卫星注入；null = 纯文本降级 M8-1 行为）；
    /// launchExternalEditor=外部编辑器启动委托（TASK064 P3 — ctrl+g 编辑当前输入；
    /// 输入文本 → 编辑后文本，null = 失败/取消降级丢弃；null 委托 = 能力降级 ctrl+g 无操作）；
    /// saveExportAsync=导出文件写委托（S12 — /export 确认 File 目标后写盘；导出全文 →
    /// 写入文件名（null = 失败）；null 委托 = 文件导出降级错误反馈）；
    /// searchSource=全局搜索源（S12 — ctrl+shift+f 对话框搜索后端；宿主注入 rg 执行器，
    /// null = 无后端降级提示 rg 不可用）。</summary>
    public ReplShell(
        IReplEngine engine,
        BannerItem banner,
        TextWriter output,
        bool inline = true,
        IReadOnlyList<IScrollItem>? seedMessages = null,
        IReadOnlyList<CommandSuggestion>? commands = null,
        ICodeHighlighter? codeHighlighter = null,
        ITerminalSizeSource? sizeSource = null,
        Action<string>? clipboard = null,
        string? themeName = null,
        CommandUsageMemory? commandUsage = null,
        string? workspaceRoot = null,
        Action<string, string>? onStopTask = null,
        IStreamingMarkdownProjector? streamingProjector = null,
        Func<string, Task>? saveThemeAsync = null,
        Func<string, Task<string?>>? launchExternalEditor = null,
        IInputPump? inputPump = null,
        Func<string, Task<string?>>? saveExportAsync = null,
        ISearchSource? searchSource = null)
    {
        _engine = engine;
        _banner = banner;
        _output = output;
        _inline = inline;
        _seedMessages = seedMessages ?? [];
        _commandUsage = commandUsage ?? new CommandUsageMemory();
        // S10 接线 — 分组注入：空查询 "/" 走 recently-used + 来源分组视图（有查询仍走既有匹配排序）
        _suggester = commands is { Count: > 0 } ? new SlashSuggester(commands, new CommandGrouping(_commandUsage)) : null;
        _sizeSource = sizeSource ?? new ConsoleTerminalSizeSource();
        _copyToClipboard = clipboard;
        _saveThemeAsync = saveThemeAsync;
        _inputPump = inputPump ?? new ReadKeyPump();
        _onStopTask = onStopTask;
        _launchExternalEditor = launchExternalEditor;
        _saveExportAsync = saveExportAsync;
        _theme = ThemeSelection.Resolve(themeName);
        _atFileSuggester = workspaceRoot is null ? null : new AtFileSuggester(workspaceRoot);
        _workspaceRoot = workspaceRoot;
        _searchSource = searchSource;
        // S9 接线 — spinner 终端字符集探测注入一次（会话期终端类型不变；Windows Terminal/默认 `*` 集）
        _spinner.Frames = TerminalCapabilities.DetectSpinnerFrames();
        // S8 接线 — 挂起态 BackTab 权限模式循环请求转发引擎回执通道（复用 Shift+Tab 循环实现）
        _permission.ModeCycleRequested += CyclePermissionMode;
        _transcript = new TranscriptController(BuildTranscriptSnapshot, verbose => _view.SetVerboseMode(verbose));
        _driver = new ReplBridgeDriver(_view, _spinner, _banner, BeginPermissionRequest, codeHighlighter, _tokenUsage, _sizeSource, streamingProjector);
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

    /// <summary>命令交互对话框协调器（TASK065 F2 — 宿主命令桥 Begin/Wait 消费面 + 测试断言面）。</summary>
    public CommandDialogCoordinator CommandDialog => _commandDialog;

    /// <summary>模型选择器协调器（测试断言面）。</summary>
    internal ModelPickerCoordinator ModelPicker => _modelPicker;

    /// <summary>斜杠补全状态机（测试断言面；null = 无命令清单）。</summary>
    internal SlashSuggester? Suggester => _suggester;

    /// <summary>@ 文件补全当前建议（S10 接线 — 测试断言面；null = 未激活）。</summary>
    internal IReadOnlyList<CommandSuggestion>? AtSuggestions => _atSuggestions;

    /// <summary>@ 文件补全选中索引（测试断言面）。</summary>
    internal int AtSuggestionIndex => _atSuggestionIndex;

    /// <summary>命令使用记忆（S10 接线 — 测试断言面：RecordUse 记账验证）。</summary>
    internal CommandUsageMemory CommandUsage => _commandUsage;

    /// <summary>历史反向搜索激活态（Ctrl+R — 测试断言面）。</summary>
    internal bool IsSearching => _search is not null;

    /// <summary>消息光标导航协调器（交互层 Step1 — 测试断言面）。</summary>
    internal MessageCursor CursorNav => _cursorNav;

    /// <summary>输入历史环（测试断言面）。</summary>
    internal HistoryRing History => _history;

    /// <summary>历史种子注入（宿主启动时从 transcript 加载最近输入）。</summary>
    public void SeedHistory(IEnumerable<string> entries) => _history.Seed(entries);

    /// <summary>主循环 — 每拍：排空键盘 → 排空引擎事件 → 差分上屏 → 对齐 50ms。
    /// S9 接线 — 进入前隐藏硬件光标（DEC 25：帧内自绘输入光标，双光标错位；退出 WriteExit 尾对称恢复）；
    /// S7 接线 — 启用 bracketed paste（DEC 2004）：支持终端的粘贴内容被 200~/201~ 标记包裹整块到达
    ///（ReadKey 通道经 DrainInput 重组为 Paste 事件），退出 WriteExit 尾对称 Disable。</summary>
    public void Run()
    {
        CursorLifecycle.HideCursor(_output);
        _output.Write(DecPrivateMode.Enable(DecPrivateModeKind.BracketedPaste));
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

        // 回合结束时挂起中的命令对话框自动收尾（取消语义 — 命令回调线程解除阻塞，confirm 拒绝/prompt 取消）
        if (!_driver.IsBusy && _commandDialog.IsPending)
        {
            CompleteCommandDialog(accepted: false, promptInput: null);
        }

        // 回合结束 — 排队输入自动发送（TASK060 一问一答节奏：逐回合发队首）
        PumpQueuedInputs();

        // S10 接线 — bash 直通结果上屏（主循环线程消费并发队列）
        DrainBashResults();

        // TASK064 P3 — 外部编辑器结果回填（主循环线程消费并发队列 — FillInput 与渲染循环同线程）
        DrainExternalEditorResults();

        // S12 接线 — 导出文件写结果反馈（主循环线程消费并发队列 — 通知反馈与渲染循环同线程）
        DrainExportResults();

        // S12 接线 — 全局搜索防抖帧拍结算（查询静默满 300ms 派发 BeginSearchAsync）
        TickGlobalSearchDebounce();
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
    /// 命令确认同步桥（TASK065 F2 — 宿主 Bind 接线点：TuiCommandCallbacks.Confirm → 此处）：
    /// 挂起确认对话框 + 同步阻塞等用户按键（回合在后台线程 — 对齐 TuiPermissionBridge.Confirm 先例，
    /// 主循环模态按键解除）。仅 Begin 的轻量形态供测试注入（不阻塞）。
    /// </summary>
    public bool ConfirmCommand(string message)
    {
        BeginCommandConfirm(message);
        return _commandDialog.WaitConfirm().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 命令输入同步桥（同上 — prompt 形态；Esc 取消返回 null 对齐命令侧可空语义；
    /// kind 透传 — Password 密文形态回显 PasswordMask 遮蔽）。
    /// </summary>
    public string? PromptCommand(string message)
    {
        BeginCommandPrompt(message);
        return _commandDialog.WaitPrompt().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 命令密文输入同步桥（TASK065 F2b — readPassword 形态：Enter 提交原文/Esc 取消 null，
    /// 回显 PasswordMask 等宽遮蔽、明文仅存协调器数据层；签名同步对齐
    /// TuiCommandCallbacks.ReadPassword 的 Func&lt;string,string?&gt;，阻塞桥先例同 ConfirmCommand）。
    /// </summary>
    public string? ReadPasswordCommand(string message)
    {
        BeginCommandPrompt(message, InputRequestKind.Password);
        return _commandDialog.WaitPrompt().GetAwaiter().GetResult();
    }

    /// <summary>挂起命令确认对话框（插入滚动区，模态路由生效）。</summary>
    public void BeginCommandConfirm(string message)
    {
        _commandDialog.BeginConfirm(message);
        AddMessage(new CommandDialogItem(_commandDialog));
    }

    /// <summary>挂起命令输入对话框（插入滚动区，模态路由生效；kind — Text 明文回显 /
    /// Password 密文遮蔽回显，默认 Text 兼容既有 prompt 调用）。</summary>
    public void BeginCommandPrompt(string message, InputRequestKind kind = InputRequestKind.Text)
    {
        _commandDialog.BeginPrompt(message, kind);
        AddMessage(new CommandDialogItem(_commandDialog));
    }

    /// <summary>完成命令对话框并移除对话框项。</summary>
    private void CompleteCommandDialog(bool accepted, string? promptInput)
    {
        if (_commandDialog.IsPrompt)
        {
            _commandDialog.CompletePrompt(promptInput);
        }
        else
        {
            _commandDialog.CompleteConfirm(accepted);
        }

        RemoveDialogItem<CommandDialogItem>();
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

    /// <summary>排空键盘事件（非阻塞）— 输入泵通道（DSG042：ReadKeyPump 现役 / RawStdinPump
    /// 原始流灰度）排空已解析键统一分派；泵内归一/paste 重组语义见各实现。</summary>
    private void DrainInput() =>
        _inputPump.Drain(HandleRawKeyPress);






    /// <summary>按键归一 + 分派（对齐基准 useTextInput.mapKey 优先级顺序 — 测试经 ConsoleKeyInfo 全链注入）；
    /// 文本变化才刷新建议（导航/移动键不重置选中索引；程序化填充跳过 — 历史导航不触发补全）。</summary>
    internal void HandleKey(ConsoleKeyInfo keyInfo)
    {
        var key = ConsoleKeyAdapter.ToKeyPress(keyInfo);
        if (key is null)
        {
            return;
        }

        HandleRawKeyPress(key);
    }

    /// <summary>按键直入分派（S7 接线 — paste 重组通道 + 测试 KeyPress 注入面）—
    /// 共用 textBefore/建议刷新链（paste 后文本变化同样刷新 @ 文件补全）。</summary>
    internal void HandleRawKeyPress(KeyPress key)
    {
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
    /// （交互层 Step1：光标激活时 ↑↓/kj/Shift 跳转/Enter 展开/Esc 两级退出）→ 后台任务面板
    /// 选中导航模态（M8-8 登记遗留：选中态激活时 ↑↓/kj 移动/Enter 查看/x 停止/Esc 退出，
    /// 其余键吞掉 — 与光标导航互斥且后于其分派）→ 补全激活专属键
    /// （Up/Down 导航/Tab 接受/Enter 执行/Esc 关闭，基准 Autocomplete 上下文优先于 Chat）
    /// → transcript 全屏模态（S6 收敛：Controller.IsActive 时整屏独占 — 激活时光标态不可达
    /// （进入被建议后分派拦截），j/k 归 transcript less 键位）
    /// → 帮助面板模态（S6：空闲态 '?' 弹出/Esc 关闭）
    /// ，其余 Emacs 编辑（Ctrl/Meta 词操作 + kill ring），控制键走编辑/提交/权限，字符插入。</summary>
    private void HandleKeyPress(KeyPress key)
    {
        // M2 序列前缀清除 — 非 ctrl 键（打字/箭头/Esc 等）插断 ctrl+x 序列：
        // 防陈旧前缀被后续 ctrl+e 误触外部编辑器（HandleCtrlKey 入口只清 ctrl 键路径）
        if (!key.Ctrl && _ctrlXPrefix)
        {
            _ctrlXPrefix = false;
        }

        if (_permission.IsPending)
        {
            HandlePermissionKey(key);
            return;
        }

        if (_commandDialog.IsPending)
        {
            HandleCommandDialogKey(key);
            return;
        }

        if (_modelPicker.IsPending)
        {
            HandleModelPickerKey(key);
            return;
        }

        // S12 四域接线 — 三对话框模态统一插位（ModelPicker 之后 Search 之前 — 任务契约链序：
        // Permission > ModelPicker > 三对话框 > Search > Cursor > Suggestion > Transcript > Help > 编辑；
        // 对话框互斥天然由链序保证 — 模态激活期间其余对话框触发键被本分支吞掉）
        if (_exportDialog is not null)
        {
            HandleExportDialogKey(key);
            return;
        }

        if (_quickOpenDialog is not null)
        {
            HandleQuickOpenKey(key);
            return;
        }

        if (_globalSearchDialog is not null)
        {
            HandleGlobalSearchKey(key);
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

        // M8-8 登记遗留收口 — 后台任务详情对话框模态（选中态 Enter 挂载，Esc/left 关闭 —
        // 对齐 permission/modelPicker 模态优先先例）：插入选中态之前（对话框激活期间全部按键
        // 路由到对话框分支吞掉；选中态保持激活，关闭后无缝回选中导航继续操作 — Enter 挂载
        // 不可重入：本分支拦截 Enter 不再触发 ShowSelectedBackgroundTask）
        if (_backgroundTaskDetailOpen)
        {
            HandleBackgroundTaskDetailKey(key);
            return;
        }

        // M8-8 登记遗留 — 后台任务面板选中导航模态（对齐 _cursorNav 轻量模态形态 — 纯状态机
        // 不新增对话框项）：置于光标导航之后（两模态互斥，光标导航优先 — 任务选中态激活时
        // Shift+Up 等键在本分支被吞，不会误入光标导航）、补全之前（选中态吞全部字符键 —
        // 模态语义，字符不进输入框不触发补全）
        if (_view.BackgroundTasks.IsSelectionActive)
        {
            HandleBackgroundTaskSelectionKey(key);
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

        // M8-8 后续 — 空闲态 x 停止后台任务（置于帮助面板之后：面板有 running 条目时 x 优先停止，
        // 无 running 时 x 落入下方字符插入 — 打字语义不受影响）
        if (IsStopTaskTrigger(key))
        {
            StopRunningBackgroundTask();
            return;
        }

        // M8-8 登记遗留 — Shift+Down 进入后台任务面板选中态（基准 useBackgroundTaskNavigation.ts:181-189：
        // 有后台任务时进入导航；与下方 Shift+Up 进消息光标导航对称）。门槛 = 空闲空输入 + 面板有
        // running 条目（忙时/打字中不进入 — 回合中面板仍渲染但不可交互；无 running 落入下方
        // 历史导航；不需停止委托 — 导航与查看无外部依赖，x 停止在选中态内降级无操作）
        if (key is { Kind: KeyKind.DownArrow, Shift: true, Ctrl: false, Meta: false }
            && !IsBusy
            && _input.Text.Length == 0
            && _view.BackgroundTasks.HasRunning)
        {
            BeginBackgroundTaskSelection();
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

        // S12 四域接线 — ctrl+shift+p 快速打开 / ctrl+shift+f 全局搜索触发键位
        //（键位语义参考 ctrl+o/ctrl+g 先例；置于 Ctrl 字符分派之前 — HandleCtrlKey 的
        // "p"/"f" case 是既有编辑/滚动语义，Shift 叠加位是触发判定的区分键；
        // 忙时/输入中文本均可打开 — 对话框挂滚动区不打断回合、输入草稿保留）
        if (IsQuickOpenTrigger(key))
        {
            BeginQuickOpen();
            return;
        }

        if (IsGlobalSearchTrigger(key))
        {
            BeginGlobalSearch();
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
            case KeyKind.Return when key.Shift:
                // S7 接线 — 基准 multiline：shift+enter 插入换行不提交（enter 才发送）
                _input.InsertNewline();
                return;
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
            case KeyKind.UpArrow when _input.IsMultiline && _input.CursorLineIndex > 0:
                // S7 接线 — 基准 multiline：多行输入内上下键优先行间垂直移动（首行内回退历史）
                _input.MoveCursorVertically(-1);
                return;
            case KeyKind.DownArrow when _input.IsMultiline && _input.CursorLineIndex < _input.LineCount - 1:
                // S7 接线 — 基准 multiline：末行内回退历史导航
                _input.MoveCursorVertically(1);
                return;
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
            case KeyKind.Paste:
                // S7 接线 — bracketed paste 整块到达（KeyPressParser 通道 VT 序列 / ReadKey 通道重组）：
                // InputBuffer.Paste 清洗 + 超阈值折叠登记 [Pasted text #N]（提交取 ExpandedText 全文）；
                // 可见行数上限随终端高度自适应（基准 rows-10 语义）
                _input.Paste(key.Input, Math.Max(0, _sizeSource.Current.Rows - 10));
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

    /// <summary>Ctrl 组合分派（对齐基准 handleCtrl 表）。
    /// M2 新增 ctrl+x ctrl+e 第二键位（基准 defaultBindings.ts:83-84 chat:externalEditor —
    /// ctrl+x 为前缀键：置 _ctrlXPrefix 序列标记，紧随其后的 ctrl+e 触发外部编辑器；
    /// 入口捕获复位 — 任何其他 ctrl 键到达即作废前缀态，无超时简化；
    /// 前缀态优先于 End() 编辑语义 — 单独 ctrl+e 仍是行尾，语义不破坏）。</summary>
    private void HandleCtrlKey(string name)
    {
        // ctrl+x 前缀捕获复位（M2 — 进入即复位：仅 "e" 分支消费触发；
        // "x" 分支重新置位延续等待，其余 ctrl 键到达即作废前缀态）
        var ctrlXPrefix = _ctrlXPrefix;
        _ctrlXPrefix = false;
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
                // M2 — 前缀态优先劫持进外部编辑器；非前缀保持 End() 编辑语义（单独 ctrl+e 仍是行尾）
                if (ctrlXPrefix)
                {
                    LaunchExternalEditor();
                }
                else
                {
                    _input.End();
                }

                break;
            case "f":
                _input.Right();
                break;
            case "g":
                // TASK064 P3 — 基准 defaultBindings.ts:83-84 ctrl+g/ctrl+x ctrl+e → chat:externalEditor → $EDITOR
                LaunchExternalEditor();
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
            case "s":
                // TASK064 P3 — 基准 chat:stash：收起当前输入（忙/闲均可用），提交后自动恢复
                StashInput();
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
            case "x":
                // M2 — ctrl+x 前缀键（仅接 ctrl+x ctrl+e 编辑器；基准 killAgents ctrl+x ctrl+k
                // 未做不预埋 — 序列标记 _ctrlXPrefix 可扩展承载后续序列）
                _ctrlXPrefix = true;
                break;
            case "y":
                _input.Yank();
                break;
            case "_":
                // S7 接线 — 基准 chat:undo 键位（ctrl+_/ctrl+shift+- 同码 \x1f，KeyPressParser 归一为 Ctrl+"_"）
                _input.Undo();
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
    /// 权限对话框模态按键（M7-3，对齐基准 CustomSelect useSelectInput + S8 键位表扩展 defaultBindings.ts
    /// Confirmation 上下文）：数字 1/2/3 直接选档（yes/dont-ask-again/no）+ Esc 取消（Deny）+ Enter 选焦点档
    /// + 上/下环绕移动焦点 + Tab 遍历（confirm:nextField）+ Space 切换选中（confirm:toggle）
    /// + Ctrl+E 解释展开（confirm:toggleExplanation）+ BackTab 权限模式循环请求（confirm:cycleMode —
    /// ModeCycleRequested 转发引擎回执通道）；其余按键全部吞掉（模态 — 不进输入缓冲、不打断回合、不退出）。
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

        // S8 接线 — Tab 遍历选项（焦点 +1 环绕）
        if (key is { Kind: KeyKind.Tab, Shift: false })
        {
            _permission.CycleFocus();
            return;
        }

        // S8 接线 — Space 切换选中态
        if (key is { Kind: KeyKind.Char, Input: " ", Ctrl: false, Meta: false })
        {
            _permission.ToggleSelection();
            return;
        }

        // S8 接线 — Ctrl+E 解释展开切换
        if (key is { Kind: KeyKind.Char, Input: "e", Ctrl: true, Meta: false })
        {
            _permission.ToggleExplanation();
            return;
        }

        // S8 接线 — BackTab 权限模式循环请求（事件转发 CyclePermissionMode 引擎回执通道）
        if (key.Kind == KeyKind.BackTab)
        {
            _permission.RequestModeCycle();
            return;
        }

        if (key is { Kind: KeyKind.Char, Ctrl: false, Meta: false, Shift: false } && key.Input is "1" or "2" or "3")
        {
            CompletePermission((PermissionChoice)(key.Input[0] - '1'));
        }
    }

    /// <summary>
    /// 命令交互对话框模态按键（TASK065 F2，对齐权限对话框交互形态）：
    /// confirm — 数字 1/2 直选（Yes/No）+ Enter 选焦点档 + 上下环绕移动焦点 + Esc 取消（No）；
    /// prompt — 字符尾追加 + Backspace 尾退格 + Enter 提交 + Esc 取消（null）；
    /// 其余按键全部吞掉（模态 — 不进输入缓冲、不打断回合、不退出）。
    /// </summary>
    private void HandleCommandDialogKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Escape)
        {
            CompleteCommandDialog(accepted: false, promptInput: null);
            return;
        }

        if (_commandDialog.IsPrompt)
        {
            HandlePromptDialogKey(key);
            return;
        }

        HandleConfirmDialogKey(key);
    }

    /// <summary>confirm 形态按键 — Enter 选焦点档 + 数字直选 + 上下环绕。</summary>
    private void HandleConfirmDialogKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Return)
        {
            CompleteCommandDialog(accepted: _commandDialog.ConfirmFocusIndex == 0, promptInput: null);
            return;
        }

        if (key.Kind == KeyKind.UpArrow)
        {
            _commandDialog.MoveConfirmFocus(-1);
            return;
        }

        if (key.Kind == KeyKind.DownArrow)
        {
            _commandDialog.MoveConfirmFocus(1);
            return;
        }

        if (key is { Kind: KeyKind.Char, Ctrl: false, Meta: false, Shift: false } && key.Input is "1" or "2")
        {
            CompleteCommandDialog(accepted: key.Input[0] == '1', promptInput: null);
        }
    }

    /// <summary>prompt 形态按键 — 字符尾追加 + 退格 + Enter 提交（空输入视为提交空串，对齐 CLI 语义）。</summary>
    private void HandlePromptDialogKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Return)
        {
            CompleteCommandDialog(accepted: false, promptInput: _commandDialog.PromptInput);
            return;
        }

        if (key.Kind == KeyKind.Backspace && !key.Ctrl && !key.Meta)
        {
            _commandDialog.BackspacePromptInput();
            return;
        }

        if (key is { Kind: KeyKind.Char, Ctrl: false, Meta: false } && key.Input.Length == 1)
        {
            _commandDialog.AppendPromptInput(key.Input[0]);
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

    /// <summary>
    /// 导出对话框模态按键（S12 四域接线 — /export 拦截挂载态）：ExportDialog.TryHandleKey
    /// 消费键位（↑↓ 环绕/1-2 直选/enter 确认/esc 取消）；Closed 终态收尾 — Confirmed 分派
    /// 目标 IO（剪贴板委托即时/文件写委托 fire-and-forget）+ RemoveDialogItem 移除对话框项；
    /// 其余按键吞掉（模态 — 不进输入缓冲、不提交、不打断）。
    /// </summary>
    private void HandleExportDialogKey(KeyPress key)
    {
        if (_exportDialog is not { } dialog)
        {
            return;
        }

        dialog.TryHandleKey(key);
        if (!dialog.Closed)
        {
            return;
        }

        var confirmed = dialog.Confirmed;
        var target = dialog.SelectedTarget;
        _exportDialog = null;
        RemoveDialogItem<ExportDialog>();
        if (confirmed)
        {
            DispatchExport(target);
        }
    }

    /// <summary>分派导出目标（S12）— Clipboard：剪贴板委托 + 通知反馈"对话已复制到剪贴板"
    ///（未注入委托降级错误反馈 — 诱导式提示）；File：文件写委托 fire-and-forget
    ///（完成结果入并发队列，DrainExportResults 主循环消费反馈文件名）。</summary>
    private void DispatchExport(ExportTarget target)
    {
        if (target == ExportTarget.Clipboard)
        {
            if (_copyToClipboard is null)
            {
                AddMessage(new ToolResultItem("导出到剪贴板失败 — 剪贴板服务不可用（宿主未注入剪贴板通道）"));
                return;
            }

            _copyToClipboard(_pendingExportText);
            _notifications.Enqueue("对话已复制到剪贴板");
            return;
        }

        if (_saveExportAsync is null)
        {
            AddMessage(new ToolResultItem("导出到文件失败 — 文件写入服务不可用（宿主未注入导出写盘通道）"));
            return;
        }

        _ = RunExportToFileAsync(_pendingExportText);
    }

    /// <summary>导出文件写异步体 — 宿主委托（返回写入文件名/null 失败）投递并发队列；
    /// 委托异常按失败入队（写盘错误不打扰渲染循环 — 主循环消费时统一反馈）。</summary>
    private async Task RunExportToFileAsync(string exportText)
    {
        string? fileName;
        try
        {
            fileName = await _saveExportAsync!(exportText).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            fileName = null;
        }

        _exportFileResults.Enqueue(fileName);
    }

    /// <summary>导出文件写结果消费（DrainEngine 每拍 — 主循环线程）：
    /// 文件名 → 通知反馈含文件名；null → 错误消息（写盘失败 + 重试诱导）。</summary>
    private void DrainExportResults()
    {
        while (_exportFileResults.TryDequeue(out var fileName))
        {
            if (fileName is null)
            {
                AddMessage(new ErrorMessageItem("导出失败 — 写入文件时出错（检查当前目录写权限后重试 /export）"));
            }
            else
            {
                _notifications.Enqueue($"已导出到 {fileName}");
            }
        }
    }

    /// <summary>ctrl+shift+p 触发判定（S12 — 基准 QuickOpenDialog 键位；ReadKey 路径
    /// ConsoleKeyAdapter 保留 Shift 位 + kitty 路径 CSI ;6 归一双通道可达）。</summary>
    private static bool IsQuickOpenTrigger(KeyPress key) =>
        key is { Kind: KeyKind.Char, Input: "p", Ctrl: true, Shift: true, Meta: false };

    /// <summary>ctrl+shift+f 触发判定（S12 — 基准 GlobalSearchDialog 键位；同上双通道归一）。</summary>
    private static bool IsGlobalSearchTrigger(KeyPress key) =>
        key is { Kind: KeyKind.Char, Input: "f", Ctrl: true, Shift: true, Meta: false };

    /// <summary>
    /// 打开快速打开对话框（S12 四域接线 — ctrl+shift+p 触发 / 宿主直调公共入口）：
    /// AtFileSuggester 全清单注入（AllFiles — MaxIndexedFiles 截断守卫在索引层）+ 挂载滚动区；
    /// 无工作区根（建议器未构造）降级诱导提示；空索引清单同（空目录/全忽略形态）；
    /// 挂载不可重入（模态期触发键被对话框吞掉 — 防御性守卫保留）。
    /// </summary>
    public void BeginQuickOpen()
    {
        if (_quickOpenDialog is not null)
        {
            return;
        }

        if (_atFileSuggester is null)
        {
            AddMessage(new ToolResultItem("快速打开不可用 — 未配置工作区根（宿主启动注入 workspaceRoot 后可用）"));
            return;
        }

        var files = _atFileSuggester.AllFiles();
        if (files.Count == 0)
        {
            AddMessage(new ToolResultItem("快速打开不可用 — 工作区无索引文件（空目录或已全部被忽略规则跳过）"));
            return;
        }

        DismissSuggestionsForDialog();
        var dialog = new QuickOpenDialog(files);
        _quickOpenDialog = dialog;
        AddMessage(dialog);
    }

    /// <summary>
    /// 快速打开对话框模态按键（S12 — QuickOpenDialog.TryHandleKey 消费键位：
    /// 字符进查询/↑↓Ctrl+P/N 导航/enter 接受/esc 关闭）；IsClosed 终态收尾 —
    /// AcceptedFile 非空插入 "@{文件} " 到输入框光标处 + 通知反馈；其余按键吞掉（模态）。
    /// </summary>
    private void HandleQuickOpenKey(KeyPress key)
    {
        if (_quickOpenDialog is not { } dialog)
        {
            return;
        }

        dialog.TryHandleKey(key);
        if (!dialog.IsClosed)
        {
            return;
        }

        var file = dialog.AcceptedFile;
        _quickOpenDialog = null;
        RemoveDialogItem<QuickOpenDialog>();
        if (file is { Length: > 0 } path)
        {
            InsertAtCursor($"@{path} ");
            _notifications.Enqueue($"已引用 {path}");
        }
    }

    /// <summary>
    /// 打开全局搜索对话框（S12 四域接线 — ctrl+shift+f 触发 / 宿主直调公共入口）：
    /// 搜索根 = 工作区根（构造注入留存）+ 搜索源（宿主 rg 执行器/测试假源）；
    /// rg 不可用（无源/探测失败）打开即通知提示（降级空结果不打扰 — 组件契约）；
    /// 挂载即静默等待首查（空查询 = 组件清场态，无需派发）；挂载不可重入（模态吞键防御）。
    /// </summary>
    public void BeginGlobalSearch()
    {
        if (_globalSearchDialog is not null)
        {
            return;
        }

        if (_workspaceRoot is not { Length: > 0 } root)
        {
            AddMessage(new ToolResultItem("全局搜索不可用 — 未配置工作区根（宿主启动注入 workspaceRoot 后可用）"));
            return;
        }

        if (_searchSource is not { IsAvailable: true })
        {
            _notifications.Enqueue("rg 不可用 — 全局搜索已禁用（安装 ripgrep 并确保在 PATH 后可用）");
        }

        DismissSuggestionsForDialog();
        _globalSearchDispatchedQuery = string.Empty;
        _globalSearchPendingSinceMs = 0;
        var dialog = new GlobalSearchDialog(root, _searchSource);
        _globalSearchDialog = dialog;
        AddMessage(dialog);
    }

    /// <summary>对话框打开即清建议态（S12 — @ 补全/斜杠下拉保持旧 token 陈旧：
    /// 对话框接受插入后输入文本变化，旧建议投影与实际 token 脱节 — 统一Dismiss 归零）。</summary>
    private void DismissSuggestionsForDialog()
    {
        _atSuggestions = null;
        _atSuggestionIndex = 0;
        _suggester?.Dismiss();
    }

    /// <summary>
    /// 全局搜索对话框模态按键（S12 — GlobalSearchDialog.TryHandleKey 消费键位：
    /// 字符/退格进查询/↑↓ 导航/enter 置 SelectedMatch/esc 置 Closed）：
    /// 查询变化记防抖时间戳（帧拍 Tick 到期派发）；SelectedMatch 非空 = enter 确认 —
    /// 关闭对话框 + 插入 "@{文件}#L{行} " 到输入框光标处 + 通知反馈；Closed = esc — 摘除收尾；
    /// 其余按键吞掉（模态 — TryHandleKey 未消费键同样不透传编辑链）。
    /// </summary>
    private void HandleGlobalSearchKey(KeyPress key)
    {
        if (_globalSearchDialog is not { } dialog)
        {
            return;
        }

        if (dialog.Closed)
        {
            RemoveGlobalSearchDialog();
            return;
        }

        var queryBefore = dialog.Query;
        dialog.TryHandleKey(key);
        if (dialog.SelectedMatch is { } match)
        {
            // enter 确认 — 组件契约"消费后调 Close 接管"：宿主取走选中项关闭并接管插入
            RemoveGlobalSearchDialog();
            InsertAtCursor($"@{match.File}#L{match.Line} ");
            _notifications.Enqueue($"已引用 {match.File}#L{match.Line}");
            return;
        }

        if (dialog.Closed)
        {
            RemoveGlobalSearchDialog();
            return;
        }

        if (dialog.Query != queryBefore)
        {
            _globalSearchPendingSinceMs = EscClock();
        }
    }

    /// <summary>移除全局搜索对话框（esc/enter 收尾）：摘除滚动区项 + 终结进行中搜索令牌
    ///（Cancel 杀孤儿 rg 进程树 — 组件 finally 收尾 + 宿主令牌 Dispose）+ 防抖态清零。</summary>
    private void RemoveGlobalSearchDialog()
    {
        _globalSearchDialog = null;
        RemoveDialogItem<GlobalSearchDialog>();
        _globalSearchCts?.Cancel();
        _globalSearchCts?.Dispose();
        _globalSearchCts = null;
        _globalSearchPendingSinceMs = 0;
        _globalSearchDispatchedQuery = string.Empty;
    }

    /// <summary>
    /// 全局搜索防抖帧拍结算（DrainEngine 每拍 — 禁 Task.Delay，对齐 ToolGroupAggregator.OnFrame
    /// 帧拍时间戳先例）：对话框挂载 && 查询有待派发（时间戳非零）&& 静默满 300ms → 派发
    /// BeginSearchAsync（查询与已派发一致时幂等跳过）。
    /// </summary>
    private void TickGlobalSearchDebounce()
    {
        if (_globalSearchDialog is not { } dialog
            || _globalSearchPendingSinceMs == 0
            || EscClock() - _globalSearchPendingSinceMs < GlobalSearchDebounceMs)
        {
            return;
        }

        _globalSearchPendingSinceMs = 0;
        if (dialog.Query == _globalSearchDispatchedQuery)
        {
            return;
        }

        DispatchGlobalSearch(dialog, dialog.Query);
    }

    /// <summary>全局搜索防抖窗口（ms — 查询静默时长；基准 DEBOUNCE_MS 的任务书取值 300）。</summary>
    internal const int GlobalSearchDebounceMs = 300;

    /// <summary>派发全局搜索（S12 — 组件不持运行中任务契约）：旧令牌先 Cancel 再 Dispose
    ///（防抖重触发杀旧 rg 进程树 — 取消为常态语义），新令牌 fire-and-forget 驱动
    /// BeginSearchAsync（异常容错 — 组件契约不抛，此处防御兜底搜索源实现缺陷）。</summary>
    private void DispatchGlobalSearch(GlobalSearchDialog dialog, string query)
    {
        _globalSearchCts?.Cancel();
        _globalSearchCts?.Dispose();
        _globalSearchCts = new CancellationTokenSource();
        _globalSearchDispatchedQuery = query;
        _ = RunGlobalSearchAsync(dialog, query, _globalSearchCts.Token);
    }

    /// <summary>全局搜索异步执行体 — 搜索源异常静默降级（组件状态机不因源实现缺陷卡死，
    /// 对齐 bash 直通容错先例）。</summary>
    private static async Task RunGlobalSearchAsync(GlobalSearchDialog dialog, string query, CancellationToken token)
    {
        try
        {
            await dialog.BeginSearchAsync(query, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            // 搜索源缺陷防御兜底 — Searching 标记可能残留（组件契约不抛的前置下不可达）
        }
    }

    /// <summary>光标处插入文本（S12 — QuickOpen/GlobalSearch 接受项引用插入）：
    /// 中部插入光标落插入段末尾（ReplaceAt 语义）；程序化填充标记防触发建议刷新
    ///（插入段尾随空格 — @token 扫描遇空白即无补全语义）。</summary>
    private void InsertAtCursor(string insertion)
    {
        var cursor = Math.Clamp(_input.CursorCol, 0, _input.Text.Length);
        var replaced = $"{_input.Text[..cursor]}{insertion}{_input.Text[cursor..]}";
        _programmaticFill = true;
        _input.ReplaceAt(replaced, cursor + insertion.Length);
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

    /// <summary>x 停止触发判定（M8-8 后续 — 对齐 IsHelpTrigger '?' 形态）— 空闲态（非忙 + 空输入 +
    /// 无修饰）的裸 'x' 且面板有运行中条目且停止委托已注入（三门槛任一不满足 x 走正常字符插入 —
    /// 打字优先，斜杠补全激活态在分派链更早拦截不可达此处）。</summary>
    private bool IsStopTaskTrigger(KeyPress key) =>
        key is { Kind: KeyKind.Char, Input: "x", Ctrl: false, Meta: false, Shift: false }
        && _input.Text.Length == 0
        && !IsBusy
        && _onStopTask is not null
        && _view.BackgroundTasks.HasRunning;

    /// <summary>停止后台任务（M8-8 后续 — 基准 BackgroundTasksDialog.tsx:268-289 x 停止）：
    /// 目标 = 选中条目（仅 running 可选）回退首个 running 条目（首版无选中导航键位 — 选中态
    /// API 已备，导航键位与历史导航冲突留后续）；经注入委托 fire-and-forget 转发引擎泵
    /// （cancelled 终态 upsert 由泵推回面板）+ 通知队列反馈。</summary>
    private void StopRunningBackgroundTask()
    {
        var target = _view.BackgroundTasks.SelectedEntry ?? FirstRunningTask();
        if (target is not { } entry)
        {
            return;
        }

        _onStopTask?.Invoke(entry.Kind, entry.TaskId);
        _notifications.Enqueue($"已请求停止后台任务（{entry.Kind}）");
    }

    /// <summary>首个运行中条目（无选中回退目标 — 插入序首个 running；IsStopTaskTrigger 已保证
    /// HasRunning，此处必命中，null 仅为防御兜底）。</summary>
    private (string Kind, string TaskId)? FirstRunningTask()
    {
        foreach (var task in _view.BackgroundTasks.Tasks)
        {
            if (task.State == BackgroundTaskPanel.RunningState)
            {
                return (task.Kind, task.TaskId);
            }
        }

        return null;
    }

    /// <summary>
    /// 后台任务面板选中导航模态按键（M8-8 登记遗留 — 基准 BackgroundTasksDialog.tsx 导航态 +
    /// useBackgroundTaskNavigation.ts:181-189，对齐 HandleCursorNavKey 轻量模态形态）：
    /// ↑/↓/k/j 相邻 running 条目移动（MoveSelection 跳终态 + 窗口跟随）、Enter 查看选中任务
    /// （详情对话框挂载 — ShowSelectedBackgroundTask，M8-8 登记遗留收口升级）、x 停止选中（复用
    /// StopRunningBackgroundTask — SelectedEntry 终态守卫回退首个 running；选中态为用户
    /// 主动保持的模态，忙时不强制退出，后台任务与前台回合独立可停）、Esc 退出选中态
    /// （ClearSelection 归 -1 + 窗口归 0）、left 退出选中态（D 批次遗留 left 退出键 — 基准
    /// BackgroundTasksDialog left 关闭语义；对话框态的 left 由 HandleBackgroundTaskDetailKey
    /// 消费关闭对话框，两态互斥不冲突）；其余按键全部吞掉（模态 — 字符不进输入缓冲）。
    /// </summary>
    private void HandleBackgroundTaskSelectionKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Escape)
        {
            _view.BackgroundTasks.ClearSelection();
            return;
        }

        if (key.Kind == KeyKind.Return)
        {
            ShowSelectedBackgroundTask();
            return;
        }

        switch (key.Kind)
        {
            case KeyKind.UpArrow:
                _view.BackgroundTasks.MoveSelection(-1);
                return;
            case KeyKind.DownArrow:
                _view.BackgroundTasks.MoveSelection(1);
                return;
            case KeyKind.LeftArrow:
                // D 批次遗留 left 退出键（基准 BackgroundTasksDialog left 关闭）— 退出选中态回常态
                _view.BackgroundTasks.ClearSelection();
                return;
            case KeyKind.Char when !key.Ctrl && !key.Meta && key.Input is "k":
                _view.BackgroundTasks.MoveSelection(-1);
                return;
            case KeyKind.Char when !key.Ctrl && !key.Meta && key.Input is "j":
                _view.BackgroundTasks.MoveSelection(1);
                return;
            case KeyKind.Char when !key.Ctrl && !key.Meta && key.Input is "x":
                StopRunningBackgroundTask();
                return;
            default:
                // 模态：其余键吞掉（不进输入缓冲 — 选中态激活期间打字暂停）
                return;
        }
    }

    /// <summary>进入后台任务面板选中态（Shift+Down — MoveSelection(+1) 无选中取首个 running 条目；
    /// 键位门槛已由分派链进入点保证 HasRunning，此处必命中）。</summary>
    private void BeginBackgroundTaskSelection() => _view.BackgroundTasks.MoveSelection(1);

    /// <summary>Enter 查看选中任务详情（M8-8 登记遗留收口 — 升级为详情对话框挂载滚动区：
    /// BackgroundTaskDetailDialog 快照构造（Entry + 墙钟），对齐 ModelPicker 挂载模式
    /// AddMessage + 关闭时 RemoveDialogItem 移除；查看后保持选中态（对话框关闭回选中态
    /// 可继续导航 — 对话框激活期间按键路由见 HandleBackgroundTaskDetailKey；挂载不可重入 —
    /// 分派链对话框分支拦截 Enter）。无选中/索引越界防御 no-op。</summary>
    private void ShowSelectedBackgroundTask()
    {
        var tasks = _view.BackgroundTasks.Tasks;
        if (_view.BackgroundTasks.SelectedIndex is >= 0 and var index && index < tasks.Count)
        {
            _backgroundTaskDetailOpen = true;
            AddMessage(new BackgroundTaskDetailDialog(tasks[index], Environment.TickCount64));
        }
    }

    /// <summary>后台任务详情对话框模态按键（对齐 HandleHelpKey 轻量模态形态 — 详情只读无交互，
    /// 数据快照构造无协调器）：Esc / left 关闭对话框（置 false + RemoveDialogItem 移除滚动区项 —
    /// 回选中态继续导航，选中态全程保持激活）；其余按键全部吞掉（模态 — 字符不进输入缓冲）。</summary>
    private void HandleBackgroundTaskDetailKey(KeyPress key)
    {
        if (key.Kind is KeyKind.Escape or KeyKind.LeftArrow)
        {
            _backgroundTaskDetailOpen = false;
            RemoveDialogItem<BackgroundTaskDetailDialog>();
        }
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

    /// <summary>补全激活时的专属键拦截（基准 Autocomplete 上下文 + S10 @ 文件补全）：
    /// 斜杠下拉激活：Up/Down 循环导航、Tab 接受不执行、Enter 接受并提交、Esc 仅关建议（优先于打断/清输入/退出）；
    /// @ 文件下拉激活（斜杠未激活时 — 互斥）：Up/Down 导航、Tab/Right 接受替换 @token、Esc 仅关 @ 建议；
    /// ghost 激活（下拉互斥为空）：RightArrow（光标在末尾）接受幽灵补全、Esc 仅关 ghost；
    /// 其余键返回 false 走正常编辑路径（编辑后刷新建议）。</summary>
    private bool HandleSuggestionKey(KeyPress key)
    {
        // S10 接线 — @ 文件补全激活态（斜杠建议优先 — 其激活时 @ 投影为空不可达）
        if (_atSuggestions is { Count: > 0 } && _suggester is not { IsActive: true })
        {
            switch (key.Kind)
            {
                case KeyKind.UpArrow:
                    _atSuggestionIndex = (_atSuggestionIndex - 1 + _atSuggestions.Count) % _atSuggestions.Count;
                    return true;
                case KeyKind.DownArrow:
                    _atSuggestionIndex = (_atSuggestionIndex + 1) % _atSuggestions.Count;
                    return true;
                case KeyKind.Tab:
                case KeyKind.RightArrow when _input.CursorCol == _input.Text.Length:
                case KeyKind.Return:
                    // 基准 typeahead 语义：接受补全 = 插入选择（Enter 不顺带提交 — 用户继续编辑）
                    AcceptAtSuggestion();
                    return true;
                case KeyKind.Escape:
                    _atSuppressedToken = CurrentAtToken(_input.Text, _input.CursorCol) ?? string.Empty;
                    _atSuggestions = null;
                    return true;
                default:
                    return false;
            }
        }

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

    /// <summary>刷新建议（用户编辑路径；建议状态机内部判定触发条件）+ S10 @ 文件补全刷新
    ///（斜杠建议优先互斥 — 其激活时 @ 投影清空）。</summary>
    private void UpdateSuggestions()
    {
        _suggester?.Update(_input.Text);
        UpdateAtSuggestions();
    }

    /// <summary>@ 文件补全刷新（S10 接线 — 基准 useTypeahead fileSuggestions 分支）：
    /// 光标处前最近 @token（token 内无空白）且斜杠建议未激活时投影建议清单
    ///（CommandSuggestion 形态：Name=相对路径，喂 SuggestionBox 复用渲染）；
    /// 抑制 token 解除（Esc 关闭后输入变化才重新建议）；token 变化时选中索引重置。</summary>
    private void UpdateAtSuggestions()
    {
        var token = CurrentAtToken(_input.Text, _input.CursorCol);
        if (token is null || token == _atSuppressedToken || _suggester is { IsActive: true } || _atFileSuggester is null)
        {
            _atSuggestions = null;
            _atSuggestionIndex = 0;
            if (token is null || token != _atSuppressedToken)
            {
                _atSuppressedToken = string.Empty;
            }

            return;
        }

        var suggestions = _atFileSuggester.Suggest(token);
        if (suggestions.Count == 0)
        {
            _atSuggestions = null;
            _atSuggestionIndex = 0;
            return;
        }

        _atSuggestions = suggestions
            .Select(static file => new CommandSuggestion(file.RelativePath, string.Empty, [], IsHidden: false))
            .ToList();
        _atSuggestionIndex = 0;
    }

    /// <summary>提取光标处前最近的 @token（S10 接线）— 从光标向前扫描到文本头或空白（空格/换行），
    /// 遇 '@' 返回其后到光标的片段；无 '@'/跨空白返回 null（裸 @ 空 token 也触发 — 全部文件列举）。</summary>
    private static string? CurrentAtToken(string text, int cursorCol)
    {
        var clamped = Math.Clamp(cursorCol, 0, text.Length);
        for (var i = clamped - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (ch is ' ' or '\n' or '\t' or '\r')
            {
                return null;
            }

            if (ch == '@')
            {
                return text[(i + 1)..clamped];
            }
        }

        return null;
    }

    /// <summary>接受选中的 @ 文件建议（S10 接线）— 光标前 @token 替换为 @+选中相对路径
    ///（ReplaceAt 光标置替换段末尾，中部替换不跳尾）；程序化填充标记防触发建议刷新
    ///（替换后 token 已是完整路径，无继续补全语义）；建议态清空。</summary>
    private void AcceptAtSuggestion()
    {
        if (_atSuggestions is not { Count: > 0 } || CurrentAtToken(_input.Text, _input.CursorCol) is not { } token)
        {
            return;
        }

        var selected = _atSuggestions[Math.Clamp(_atSuggestionIndex, 0, _atSuggestions.Count - 1)].Name;
        var tokenStart = _input.CursorCol - token.Length - 1;
        var replaced = $"{_input.Text[..(tokenStart + 1)]}{selected}{_input.Text[_input.CursorCol..]}";
        _programmaticFill = true;
        _input.ReplaceAt(replaced, tokenStart + 1 + selected.Length);
        _atSuggestions = null;
        _atSuggestionIndex = 0;
    }

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
                var merged = string.Join("\n", _queuedInputs.Select(static entry => entry.Display));
                _queuedInputs.Clear();
                _view.QueuedInputs = [];
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

    /// <summary>提交输入（TASK060 一问一答节奏 + TASK062 后续排队面板 + S7 提交全文）：空文本忽略；
    /// 未登录本地反馈（基准 ⎿ Not logged in — 不调引擎不傻等）；忙时改排队（基准 queued 语义）—
    /// **不上滚动区**（输入栏上方 queued 面板渲染 — TASK062 用户验收反馈：滚动区排队提示破坏消息流），
    /// 入历史环，回合结束自动按序发送（DrainEngine 队列泵 — 发送时才上屏用户消息）；闲时正常直发。
    /// S7 提交全文：显示层用折叠形态（[Pasted text #N]），发引擎取 ExpandedText（粘贴引用展开回原文）—
    /// 上屏/历史/打断恢复保留显示形态（折叠可见语义）。
    /// TASK064 P3 — ctrl+s 暂存恢复：提交动作发生后（输入框已清空，含排队/本地拦截/登录拒绝全部路径）
    /// 暂存单槽文本自动回填输入框（外壳捕获 + 主流程统一收尾 — 见 SubmitCore/RestoreStashedInput）。</summary>
    internal void Submit()
    {
        var text = _input.Text;
        if (text.Length == 0)
        {
            return;
        }

        var expanded = _input.ExpandedText;
        _input.Clear();
        // TASK064 P3 — 捕获并清空暂存单槽（提交即消费：主流程结束后统一回填，空文本早退不消费）
        var stashed = _stashedInput;
        _stashedInput = string.Empty;
        SubmitCore(text, expanded);
        RestoreStashedInput(stashed);
    }

    /// <summary>提交主流程（Submit 外壳剥离暂存收尾后的全部路径 — bash 直通/主题拦截/使用记账/
    /// 登录门/忙时排队/闲时直发；各 return 路径均为"提交动作已发生"，外壳统一恢复暂存）。</summary>
    private void SubmitCore(string text, string expanded)
    {
        _history.Add(text);
        _history.Reset();
        // S10 接线 — `!` 前缀 bash 直通（基准 inputModes bash 模式：剥前缀本地执行不进引擎；
        // busy 态不挡 — 直通命令独立于回合状态，30s 超时守卫在执行器侧）
        if (text.StartsWith('!') && text.Length > 1)
        {
            AddMessage(new UserMessageItem(text));
            RunBashDirect(text[1..]);
            return;
        }

        // S9 接线 — /theme 拦截：TUI 本地切换渲染主题（不透传引擎 — 引擎侧 /theme 的
        // TerminalHelper.ClearScreen 会破坏 TUI 渲染流；会话级切换即时生效，配置持久化留宿主通道）；
        // 用户命令上屏与引擎路径视觉一致
        if (text == ThemeCommandPrefix || text.StartsWith(ThemeCommandPrefix + " ", StringComparison.Ordinal))
        {
            AddMessage(new UserMessageItem(text));
            HandleThemeCommand(text);
            return;
        }

        // S12 接线 — /export 拦截：TUI 本地导出（不透传引擎 — 导出为客户端动作无引擎语义）；
        // 快照先于命令上屏拍摄（命令自身 UserMessageItem 不进导出文本），挂载 ExportDialog 模态确认目标
        if (text == ExportCommandPrefix || text.StartsWith(ExportCommandPrefix + " ", StringComparison.Ordinal))
        {
            var exportText = TranscriptExporter.Export(_view.Messages.Items);
            AddMessage(new UserMessageItem(text));
            HandleExportCommand(text, exportText);
            return;
        }

        // S10 接线 — 斜杠命令使用记账（提交即使用：TUI 无引擎侧执行成败回执通道，
        /// recently-used 分组语义 = 用户常用命令；命令名取首个空格前段）
        if (text.StartsWith('/') && _suggester is not null)
        {
            var commandName = text.Split(' ', 2)[0][1..];
            if (_suggester.IsKnownCommand(commandName))
            {
                _commandUsage.RecordUse(commandName);
            }
        }

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
                _view.QueuedInputs = _queuedInputs.Select(static entry => entry.Display).ToList();
                return;
            }

            _queuedInputs.Add((text, expanded));
            _view.QueuedInputs = _queuedInputs.Select(static entry => entry.Display).ToList();
            return;
        }

        AddMessage(new UserMessageItem(text));
        _lastSubmittedText = text;
        _lastSubmittedIndex = _view.Messages.Count - 2;
        _engine.Submit(expanded);
    }

    /// <summary>/theme 拦截命令前缀（S9 接线 — GeneratedSlashCommandCatalog 是源码生成静态表，
    /// 动态注册不可行，ReplShell 层拦截本地切换渲染色板 + 通知反馈）。</summary>
    private const string ThemeCommandPrefix = "/theme";

    /// <summary>/export 拦截命令前缀（S12 四域接线 — 同 /theme 拦截形态：
    /// ReplShell 层本地拦截挂载导出对话框，命令清单静态表不感知）。</summary>
    private const string ExportCommandPrefix = "/export";

    /// <summary>导出命令处理（S12）— "/export"：消息快照（SubmitCore 拦截分支已拍平 —
    /// 先于命令上屏，命令自身不进导出文本；string 不可变 = 防御拷贝，挂载后滚动区变化
    /// 不影响导出内容）+ 挂载 ExportDialog 模态（enter/数字确认导出目标）；
    /// "/export &lt;参数&gt;"：参数诱导提示（无参唯一合法形态 — 错误提示带正确写法）；
    /// 空会话（导出全文为空）提示无可导出内容不挂载。</summary>
    private void HandleExportCommand(string text, string exportText)
    {
        var argument = text.Length > ExportCommandPrefix.Length
            ? text[(ExportCommandPrefix.Length + 1)..].Trim()
            : string.Empty;
        if (argument.Length > 0)
        {
            AddMessage(new ToolResultItem($"未知参数「{argument}」— /export 不接受参数（直接运行 /export 打开导出目标对话框：剪贴板/文件）"));
            return;
        }

        if (exportText.Length == 0)
        {
            AddMessage(new ToolResultItem("无可导出的对话内容 — 先进行对话后再运行 /export"));
            return;
        }

        _pendingExportText = exportText;
        _exportDialog = new ExportDialog();
        AddMessage(_exportDialog);
    }

    /// <summary>主题命令处理（S9 接线）— "/theme"：当前清单提示；"/theme &lt;名称&gt;"：合法名切换
    /// （ThemeSelection.Resolve，下一帧渲染色板生效）+ 通知反馈"已切换主题"；未知名错误提示含合法值清单
    ///（错误提示带诱导：为什么拒绝/合法写法，符合错误提示规范）。</summary>
    private void HandleThemeCommand(string text)
    {
        var names = string.Join(" ", ThemeSelection.Names);
        var argument = text.Length > ThemeCommandPrefix.Length
            ? text[(ThemeCommandPrefix.Length + 1)..].Trim()
            : string.Empty;
        if (argument.Length == 0)
        {
            AddMessage(new ToolResultItem($"/theme 可用主题: {names}（用法 /theme &lt;名称&gt;）"));
            return;
        }

        if (!ThemeSelection.Names.Contains(argument))
        {
            AddMessage(new ToolResultItem($"未知主题「{argument}」— 合法值: {names}（大小写敏感，如 /theme dark）"));
            return;
        }

        _theme = ThemeSelection.Resolve(argument);
        _notifications.Enqueue($"已切换主题: {argument}");
        // S11 遗留#1 — 持久化委托 fire-and-forget（切换即时生效不被 IO 阻断；委托内部容错静默）
        _ = PersistThemeAsync(argument);
    }

    /// <summary>主题持久化（宿主委托写回 settings.json）— null/异常静默降级（会话级生效兜底，
    /// 持久化失败不打扰用户 — 通知反馈已发，写盘失败下次启动回退原主题）。</summary>
    private async Task PersistThemeAsync(string themeName)
    {
        try
        {
            if (_saveThemeAsync is { } save)
            {
                await save(themeName).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // 持久化通道容错 — 配置服务写盘失败静默（降级会话级）
        }
    }

    /// <summary>bash 直通执行（S10 接线 — fire-and-forget 异步执行：完成结果入并发队列，
    /// DrainEngine 消费上屏；30s 超时守卫 CancellationTokenSource，超时杀进程树返回部分输出 + 中断标记）。</summary>
    private void RunBashDirect(string command) =>
        _ = ExecuteBashDirectAsync(command);

    /// <summary>bash 直通异步执行体 — 结果投递并发队列（线程池线程完成，不触 UI 状态）。</summary>
    private async Task ExecuteBashDirectAsync(string command)
    {
        try
        {
            using var timeout = new CancellationTokenSource(BashDirectTimeout);
            var result = await _bash.ExecuteAsync(command, timeout.Token).ConfigureAwait(false);
            _bashResults.Enqueue(result);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // 执行器契约不抛（启动失败降级 StdErr 消息）；此处防御兜底 — 异常结果同样入队上屏
            _bashResults.Enqueue(new BashDirectResult(string.Empty, $"命令执行异常: {ex.Message}", -1, false));
        }
    }

    /// <summary>bash 直通结果消费（DrainEngine 每拍 — 主循环线程上屏，对齐基准 processBashCommand 形态）：
    /// 退出码 0 → assistant 样式（stdout+stderr 拼接）；非 0 → error 样式（基准 ShellError error 分支）；
    /// 中断 → 超时提示（基准 createUserInterruptionMessage 语义）。</summary>
    private void DrainBashResults()
    {
        while (_bashResults.TryDequeue(out var result))
        {
            if (result.Interrupted)
            {
                AddMessage(new ErrorMessageItem($"命令超时（{BashDirectTimeout.TotalSeconds:0}s）已中断"));
                continue;
            }

            var output = JoinBashOutput(result.StdOut, result.StdErr);
            if (result.ExitCode == 0)
            {
                AddMessage(new AssistantMessageItem(output.Length > 0 ? output : "（无输出）"));
            }
            else
            {
                AddMessage(new ErrorMessageItem(output.Length > 0
                    ? $"{output}（退出码 {result.ExitCode}）"
                    : $"命令失败（退出码 {result.ExitCode}）"));
            }
        }
    }

    /// <summary>拼接 stdout/stderr（基准 bash-stdout + bash-stderr 两段拼接 — 尾部空白裁剪，空段跳过）。</summary>
    private static string JoinBashOutput(string stdout, string stderr)
    {
        var parts = new[] { stdout.TrimEnd(), stderr.TrimEnd() }.Where(static part => part.Length > 0);
        return string.Join("\n", parts);
    }

    /// <summary>排队输入发送泵（DrainEngine 回合结束时调用）— 非忙态且有排队时取队首：
    /// 上屏用户消息（发送时才进滚动区 — 排队期间只在输入栏上方面板）+ 直发引擎（提交形态）；
    /// 逐回合发一条维持一问一答节奏。</summary>
    private void PumpQueuedInputs()    {
        if (IsBusy || _queuedInputs.Count == 0)
        {
            return;
        }

        var (display, expanded) = _queuedInputs[0];
        _queuedInputs.RemoveAt(0);
        AddMessage(new UserMessageItem(display));
        _view.QueuedInputs = _queuedInputs.Select(static entry => entry.Display).ToList();
        _engine.Submit(expanded);
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
                // S9 接线 — DEC 2026 探测结果条件包裹（不支持终端裸差分直出，对齐 FrameComposer 同一条件）
                _output.Write(_syncOutputSupported
                    ? Csi.SynchronizedOutputBegin() + output + Csi.SynchronizedOutputEnd()
                    : output);
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

    /// <summary>同步补全建议到视图（渲染前状态投影 — EnsureScreens 屏高计算依赖建议区高度，必须先行）：
    /// 斜杠建议优先；未激活时 @ 文件建议占用建议区（S10 接线 — 投影 CommandSuggestion 形态，
    /// 裸名渲染借参数模式通道，列宽 = 最长路径 + 4）。</summary>
    private void SyncSuggestionState()
    {
        if (_suggester is { IsActive: true })
        {
            _view.Suggestions = _suggester.Items;
            _view.SuggestionIndex = _suggester.SelectedIndex;
            _view.SuggestionNameColumnWidth = _suggester.NameColumnWidth;
            _view.SuggestionArgumentMode = _suggester.IsArgumentMode;
        }
        else if (_atSuggestions is { Count: > 0 })
        {
            _view.Suggestions = _atSuggestions;
            _view.SuggestionIndex = _atSuggestionIndex;
            _view.SuggestionNameColumnWidth = _atSuggestions.Max(static item => item.Name.Length) + 4;
            _view.SuggestionArgumentMode = true;
        }
        else
        {
            _view.Suggestions = null;
            _view.SuggestionIndex = 0;
            _view.SuggestionNameColumnWidth = 0;
            _view.SuggestionArgumentMode = false;
        }

        _view.GhostSuffix = _search is { Current.Length: > 0 } ? _search.Current : _suggester?.GhostSuffix ?? string.Empty;
        // S7 队列元组化 — 渲染投影显示形态（每次重投影新列表：队列实例类型与视图契约解耦）
        _view.QueuedInputs = _queuedInputs.Select(static entry => entry.Display).ToList();
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
    /// transcript 残留防御（引擎死亡路径已先退出，此处兜底）：清屏复位（transcript 态无 scrollback 语义可保留）。
    /// S9 接线 — 尾部对称恢复硬件光标（DEC 25 show，幂等可重入；崩溃路径由宿主全局异常兜底补发）；
    /// S7 接线 — 尾部对称 Disable bracketed paste（DEC 2004 — Run 启用的对称恢复）。</summary>
    public void WriteExit()
    {
        if (!_inline || _view.Transcript is not null)
        {
            WriteExitSequences(Sgr.Reset + Csi.EraseScreen() + Csi.CursorPosition(1, 1) + CursorLifecycle.Show);
            return;
        }

        // 光标在输入位（上帧 frameEnd = 光标所在逻辑行）→ 上移（光标行距顶线 + 顶线距间隔行）到间隔行 →
        // 清 TailHeight 行 → 内容底行换行（S7 多行化：上移量随光标逻辑行增长，单行 = 既有 2 行语义）
        var tail = _view.TailHeight;
        var up = _input.CursorLineIndex + 2;
        WriteExitSequences(Sgr.Reset + Csi.CursorUp(up) + "\r" + string.Concat(Enumerable.Repeat(Csi.EraseLine() + "\r\n", tail - 1)) + Csi.EraseLine() + "\r\n" + CursorLifecycle.Show);

        // S10 接线 — 命令使用记忆持久化（fire-and-forget + try-catch 静默：退出路径不因持久化失败阻塞/报错）
        _ = SaveCommandUsageAsync();
    }

    /// <summary>退出序列发射公共收尾（S7 接线 — 统一追加 bracketed paste Disable + Flush）。</summary>
    private void WriteExitSequences(string sequences)
    {
        _output.Write(sequences + DecPrivateMode.Disable(DecPrivateModeKind.BracketedPaste));
        _output.Flush();
    }

    /// <summary>命令使用记忆持久化（S10 接线 — ~/.jcc/command-history.json 原子写；持久化失败静默
    ///（IO 权限等，下次会话降级空记忆不阻塞启动，HistorySeeder 先例语义））。</summary>
    private async Task SaveCommandUsageAsync()
    {
        try
        {
            await _commandUsage.SaveAsync(CommandUsageMemory.DefaultFilePath).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
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

    /// <summary>ctrl+s 输入暂存（TASK064 P3 — 基准 defaultBindings.ts:85 chat:stash +
    /// PromptInputStashNotice.tsx:18）：收起当前输入腾出输入框（忙/闲均可用 — 暂存非忙时专属），
    /// 单槽覆盖式（重复暂存覆盖旧槽，非栈）；反馈消息上屏（PostInfo 先例 — 滚动区 dim 消息，
    /// 首版不做 footer 通知队列）；空输入无操作（不覆盖已有暂存）。
    /// 恢复条件 = 下次 Submit 提交动作完成后自动回填（见 Submit/RestoreStashedInput）。</summary>
    private void StashInput()
    {
        var text = _input.Text;
        if (text.Length == 0)
        {
            return;
        }

        _stashedInput = text;
        _input.Clear();
        PostInfo("已暂存（提交后自动恢复）");
    }

    /// <summary>提交后暂存回填（TASK064 P3 — Submit 外壳统一收尾：提交动作发生
    ///（含忙时排队/本地拦截/登录拒绝全部路径）后暂存文本填回输入框，光标落文本尾；空槽无操作）。</summary>
    private void RestoreStashedInput(string stashed)
    {
        if (stashed.Length > 0)
        {
            FillInput(stashed, cursorToStart: false);
        }
    }

    /// <summary>ctrl+g 打开外部编辑器编辑当前输入（TASK064 P3 — 基准 defaultBindings.ts:83-84
    /// ctrl+g/ctrl+x ctrl+e → $EDITOR）：空输入/委托未注入/编辑会话进行中均无操作；
    /// fire-and-forget 异步等待（编辑器期间主循环继续渲染不冻屏 — 输入框可继续输入，
    /// 首版简化：发起时记录原文，编辑器完成后输入框未变才回填编辑结果，已变丢弃防覆盖用户编辑）。</summary>
    private void LaunchExternalEditor()
    {
        var text = _input.Text;
        if (text.Length == 0 || _launchExternalEditor is null || _externalEditorActive)
        {
            return;
        }

        _externalEditorActive = true;
        _externalEditorTask = EditInExternalEditorAsync(text);
    }

    /// <summary>外部编辑器异步等待体 — 线程池完成不触 UI 状态（结果入并发队列，
    /// DrainExternalEditorResults 主循环消费）；委托异常/失败结果静默降级丢弃（对齐 bash 直通容错先例）。</summary>
    private async Task EditInExternalEditorAsync(string originalText)
    {
        try
        {
            var edited = await _launchExternalEditor!(originalText).ConfigureAwait(false);
            _externalEditorResults.Enqueue((originalText, edited));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 编辑器通道容错 — 失败入队 null 信号（仅复位会话标记，输入框不受打扰）
            _externalEditorResults.Enqueue((originalText, null));
        }
    }

    /// <summary>外部编辑器结果消费（DrainEngine 每拍 — 主循环线程）：
    /// null = 失败/取消丢弃；输入框与发起时原文一致才回填编辑结果（已变丢弃 — 用户编辑优先，
    /// 编辑结果含空串 = 用户清空文本的合法编辑形态）。</summary>
    private void DrainExternalEditorResults()
    {
        while (_externalEditorResults.TryDequeue(out var result))
        {
            _externalEditorActive = false;
            if (result.Edited is { } edited && _input.Text == result.Original)
            {
                FillInput(edited, cursorToStart: false);
            }
        }
    }
}
