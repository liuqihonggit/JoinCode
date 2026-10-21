namespace NativeTui.Core.Repl;

/// <summary>
/// verbose 全局开关感知项（消息操作域契约）— ReplView 渲染前投影全局 verbose 态（SetVerboseMode 通道）；
/// item 侧消费（IsExpanded 联动全量渲染）在 item 域实现（S2 接线 — transcript 模式 verbose 展开）。
/// </summary>
public interface IVerboseAware
{
    /// <summary>全局 verbose 投影（true = transcript 全量渲染 — item 展开态联动入口）。</summary>
    bool Verbose { get; set; }
}

/// <summary>连续用户消息紧凑感知项（消息操作域契约）— ReplView 渲染前投影严格前驱判定；
/// item 侧消费（margin 压缩为 0）在 item 域实现（S2 接线 — UserMessageSpacing.IsUserContinuation 语义）。</summary>
public interface IUserContinuationAware
{
    /// <summary>连续用户消息投影（true = 前驱同为用户消息 → 紧凑 margin 0）。</summary>
    bool IsContinuation { get; set; }
}

/// <summary>
/// REPL 整屏视图 — 对齐基准 REPL.tsx bottom slot 布局（自底向上）：
/// footer 1-2 行（paddingX=2，通知非空时 2 行）+ 消息操作条 0-2 行（光标态激活时 — 基准
/// MessageActionsFooter，footer 紧上方；光标态建议区恒空，与基准 PromptInput 下方位置等价）
/// + 建议区 0-6 行（斜杠补全激活时，基准 PromptInputFooterSuggestions）
/// + 输入底边线 1 行 + 输入文本 1 行 + 输入顶边线 1 行（基准 PromptInput 上下全宽 ─ 分隔线）
/// + 排队面板 0-N 行（输入框上方，基准 queued）+ 后台任务面板 0-N 行（排队面板之上，M8-8 常驻 — 基准 CoordinatorAgentStatus）
/// + 间隔 1 行（输入框 marginTop=1），其余全部给消息滚动区（sticky 钉底）。
/// spinner 行（输入框上方，基准 scrollable 区尾部）由滚动区尾部 SpinnerItem 承载。
/// 消息列表域职责（S3）：未读分界（sticky 滚动跟随通道自动快照/清除 + divider 惰性挂载计数）、
/// 渲染项数上限 200（步长 50 量化裁最旧 + 头部折叠提示行）、verbose/连续用户消息渲染前投影通道
/// （IVerboseAware / IUserContinuationAware — item 域实现消费，见未决问题）。
/// </summary>
public sealed class ReplView
{
    /// <summary>渲染项数上限（超出触发头部折叠 — 基准 MAX_MESSAGES_WITHOUT_VIRTUALIZATION=200，
    /// 非虚拟化渲染内存守卫；被裁内容已滚入终端 scrollback 可回看）。</summary>
    public const int VisibleItemLimit = 200;

    /// <summary>折叠推进步长（基准 MESSAGE_CAP_STEP=50 — 量化推进，减少每帧头部裁剪位移抖动）。</summary>
    private const int FoldStep = 50;

    private readonly FooterBar _footer = new();
    private readonly PromptInputBox _inputBox = new();
    private readonly SuggestionBox _suggestionBox = new();
    private readonly MessageActionsFooter _actionsBar = new();

    /// <summary>未读分界 divider（null = 未挂载 — 分界后新消息到达时惰性挂载）。</summary>
    private UnreadDividerItem? _unreadDivider;

    /// <summary>头部折叠提示（null = 未挂载 — 渲染上限触发时挂载）。</summary>
    private FoldedNoticeItem? _foldedNotice;

    /// <summary>未读分界快照（分界时刻的滚动区项总数；-1 = 钉底态无分界）。</summary>
    private int _unreadBoundaryIndex = -1;

    /// <summary>消息滚动区（sticky 钉底）。</summary>
    public ScrollBox Messages { get; } = new(stickyScroll: true);

    /// <summary>输入框文本。</summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>光标文本内偏移（0 = 首字符之前）。</summary>
    public int CursorCol { get; set; }

    /// <summary>建议列表（null/空 = 补全未激活不渲染）。</summary>
    public IReadOnlyList<CommandSuggestion>? Suggestions { get; set; }

    /// <summary>建议选中索引。</summary>
    public int SuggestionIndex { get; set; }

    /// <summary>建议命令列宽（全量命令最长名 + 6，防布局跳动）。</summary>
    public int SuggestionNameColumnWidth { get; set; }

    /// <summary>参数补全模式（建议项为参数值，渲染不带 / 前缀）。</summary>
    public bool SuggestionArgumentMode { get; set; }

    /// <summary>幽灵文本后缀（输入中途 "/前缀" 的命令补全剩余部分 — dim 渲染在文本后）。</summary>
    public string GhostSuffix { get; set; } = string.Empty;

    /// <summary>footer 状态（模式/busy/agents 提示/登录错误/通知）。</summary>
    public FooterState Footer { get; set; } = new();

    /// <summary>排队待发送输入（TASK062 后续 — 基准 queued 面板：输入栏上方逐行 dim 渲染，不上滚动区；
    /// 回合结束自动发送时才上屏为用户消息）。</summary>
    public IReadOnlyList<string> QueuedInputs { get; set; } = [];

    /// <summary>后台任务常驻面板（M8-8 — 可变实例原地 upsert，Driver 经此引用直接更新；渲染在排队面板之上）。</summary>
    public BackgroundTaskPanel BackgroundTasks { get; } = new();

    /// <summary>消息操作条状态（null = 光标态未激活不渲染 — ReplShell 光标态渲染前投影
    /// MessageActionsState.Of(选中项)）。</summary>
    public MessageActionsState? MessageActions { get; set; }

    /// <summary>全局 verbose 开关（transcript 模式 — 渲染前经 IVerboseAware 投影给展开态 item）。</summary>
    public bool VerboseMode { get; private set; }

    /// <summary>未读计数（分界后新增项数 — divider 自身不计；钉底态恒 0）。</summary>
    public int UnreadCount => _unreadBoundaryIndex < 0
        ? 0
        : Math.Max(0, Messages.Count - _unreadBoundaryIndex - (_unreadDivider is null ? 0 : 1));

    /// <summary>建议区高度（0 = 未激活）。</summary>
    private int SuggestionHeight => _suggestionBox.MeasureHeight(Suggestions?.Count ?? 0);

    /// <summary>排队面板高度（0 = 无排队）。</summary>
    private int QueueHeight => QueuedInputs.Count;

    /// <summary>后台任务面板高度（0 = 无任务 — 空面板零渲染）。</summary>
    private int BackgroundTaskHeight => BackgroundTasks.MeasureHeight();

    /// <summary>操作条高度（0 = 光标态未激活）。</summary>
    private int ActionsHeight => MessageActions is null ? 0 : MessageActionsFooter.Height;

    /// <summary>设置全局 verbose 开关（transcript 模式消费 — 渲染时经 IVerboseAware 投影给 item 的
    /// 展开接口；item 侧 IsExpanded 联动接线在 item 域，见未决问题）。</summary>
    public void SetVerboseMode(bool verbose) => VerboseMode = verbose;

    /// <summary>标记未读分界（用户视口离开底部 — Render 时 sticky=false 自动调用；幂等 — 已有快照保持，
    /// 对齐基准 first scroll-away 快照语义）。分界后新消息到达由 SyncUnreadDivider 惰性挂载分隔线并计数。</summary>
    public void MarkUnreadBoundary()
    {
        if (_unreadBoundaryIndex < 0)
        {
            _unreadBoundaryIndex = Messages.Count;
        }
    }

    /// <summary>清除未读分界（钉底恢复 — Render 时 sticky=true 自动调用；卸载分隔线 + 快照归零；
    /// 对齐基准 repin 语义 — 回到底部后 "N 条新消息" 线不再滞留）。</summary>
    public void ClearUnreadBoundary()
    {
        if (_unreadDivider is not null)
        {
            var index = Messages.IndexOf(_unreadDivider);
            if (index >= 0)
            {
                Messages.RemoveAt(index);
            }

            _unreadDivider = null;
        }

        _unreadBoundaryIndex = -1;
    }

    /// <summary>
    /// 内容总高度（inline 模式建屏用）— 消息全量高度 + 间隔 1 + 输入区 3 + 排队面板 + 后台任务面板 + 建议区 + 操作条 + footer 高度；
    /// 屏高取此值时消息区恰好全量展示（无滚动窗口），历史自然滚入终端 scrollback。
    /// </summary>
    public int MeasureContentHeight(int cols) =>
        Messages.MeasureContentHeight(cols) + 4 + QueueHeight + BackgroundTaskHeight + SuggestionHeight + ActionsHeight + _footer.MeasureHeight(Footer);

    /// <summary>inline 模式尾部动态区行数（间隔 1 + 输入区 3 + 排队面板 + 后台任务面板 + 建议区 + 操作条 + footer 高度）— 退出清理用。</summary>
    public int TailHeight => 4 + QueueHeight + BackgroundTaskHeight + SuggestionHeight + ActionsHeight + _footer.MeasureHeight(Footer);

    /// <summary>
    /// 输入光标内容坐标（inline 模式帧尾光标位）— 行 = 输入文本行（内容底上方 footer+操作条+建议区+2），
    /// 列 = PromptWidth + 显示宽度（修复：中文宽字符 — char 偏移换算显示列，钳制行宽，右缘即 pending wrap 状态）。
    /// </summary>
    public CursorPos InputCursorPos(int contentRows, int cols, int cursorCol)
    {
        var footerHeight = _footer.MeasureHeight(Footer);
        var displayCol = PromptInputBox.MeasureDisplayWidth(Input.AsSpan(0, Math.Min(cursorCol, Input.Length)));
        return new CursorPos(Math.Min(cols, PromptInputBox.PromptWidth + displayCol), contentRows - footerHeight - ActionsHeight - SuggestionHeight - 2);
    }

    /// <summary>渲染整屏（消息区 → 后台任务面板 → 排队面板 → 输入框 → 建议区 → 操作条 → footer）。
    /// 渲染前列表域同步：未读分界（sticky 通道）→ 项投影（verbose/连续用户消息）→ 渲染上限裁剪 → divider 挂载计数。</summary>
    public void Render(Screen screen, Theme theme)
    {
        SyncUnreadBoundary();
        SyncItemProjections();
        EnforceVisibleLimit();
        SyncUnreadDivider();

        var actionsHeight = ActionsHeight;
        var footerHeight = _footer.MeasureHeight(Footer);
        var suggestionHeight = SuggestionHeight;
        var queueHeight = QueueHeight;
        var backgroundHeight = BackgroundTaskHeight;
        var footerFrame = new Rect(2, screen.Rows - footerHeight, Math.Max(0, screen.Cols - 4), footerHeight);
        _footer.Render(screen, footerFrame, theme, Footer);

        if (actionsHeight > 0 && MessageActions is { } actions)
        {
            var actionsFrame = new Rect(2, screen.Rows - footerHeight - actionsHeight, Math.Max(0, screen.Cols - 4), actionsHeight);
            _actionsBar.Render(screen, actionsFrame, theme, actions);
        }

        if (suggestionHeight > 0 && Suggestions is { Count: > 0 })
        {
            var suggestionFrame = new Rect(0, screen.Rows - footerHeight - actionsHeight - suggestionHeight, screen.Cols, suggestionHeight);
            _suggestionBox.Render(screen, suggestionFrame, theme, Suggestions, SuggestionIndex, SuggestionNameColumnWidth, SuggestionArgumentMode);
        }

        var inputFrame = new Rect(0, screen.Rows - footerHeight - actionsHeight - suggestionHeight - 3, screen.Cols, 3);
        _inputBox.Render(screen, inputFrame, theme, Input, CursorCol, Footer.Busy, GhostSuffix);

        if (queueHeight > 0)
        {
            RenderQueuePanel(screen, new Rect(0, inputFrame.Y - queueHeight, screen.Cols, queueHeight), theme);
        }

        // 后台任务面板在排队面板之上（排队输入紧贴输入框 — 现有语义零变化；常驻任务是环境状态居一层之上）
        if (backgroundHeight > 0)
        {
            BackgroundTasks.Render(screen, new Rect(0, inputFrame.Y - queueHeight - backgroundHeight, screen.Cols, backgroundHeight), theme);
        }

        var messagesHeight = Math.Max(0, screen.Rows - footerHeight - actionsHeight - suggestionHeight - queueHeight - backgroundHeight - 4);
        var messagesFrame = new Rect(0, 0, screen.Cols, messagesHeight);
        Messages.Render(screen, messagesFrame, theme);
    }

    /// <summary>渲染排队面板（基准 queued 形态 — 输入栏上方）：每条一行 ❯ 前缀 + 文本 + dim " (queued)"，
    /// 整行 dim（待发态弱化；首条不带 queued 标记 — 即将发送语义，对齐基准下一条高亮）。</summary>
    private void RenderQueuePanel(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var promptStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.PromptBorder, Dim = true });
        for (var i = 0; i < QueuedInputs.Count; i++)
        {
            var row = frame.Y + i;
            var col = screen.WriteText(row, frame.X, $"{Figures.Pointer} ", promptStyle, frame.Right);
            var suffix = i == 0 ? string.Empty : " (queued)";
            screen.WriteText(row, col, $"{QueuedInputs[i]}{suffix}", dim, frame.Right);
        }
    }

    /// <summary>未读分界 sticky 通道同步（滚动跟随状态现有通道 — ScrollBox.Sticky）：
    /// 离开底部（sticky=false）→ 快照分界；钉底恢复（sticky=true）→ 清除分隔线。</summary>
    private void SyncUnreadBoundary()
    {
        if (Messages.Sticky)
        {
            ClearUnreadBoundary();
        }
        else
        {
            MarkUnreadBoundary();
        }
    }

    /// <summary>未读分隔线挂载/计数刷新（分界后新消息到达 — 惰性挂载于分界位置，幂等；
    /// 后续新消息由驱动层 Insert 到 spinner 之前，天然落在 divider 之后）。</summary>
    private void SyncUnreadDivider()
    {
        if (_unreadBoundaryIndex < 0 || Messages.Count <= _unreadBoundaryIndex)
        {
            return;
        }

        if (_unreadDivider is null)
        {
            _unreadDivider = new UnreadDividerItem(count: 0);
            Messages.Insert(_unreadBoundaryIndex, _unreadDivider);
        }

        _unreadDivider.Count = UnreadCount;
    }

    /// <summary>渲染前项投影通道 — IVerboseAware（全局 verbose）/ IUserContinuationAware（连续用户消息紧凑）
    /// 逐项投影（item 域实现消费 — S2 接线，见未决问题）。</summary>
    private void SyncItemProjections()
    {
        var items = Messages.Items;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is IVerboseAware aware)
            {
                aware.Verbose = VerboseMode;
            }

            if (items[index] is IUserContinuationAware continuationAware)
            {
                continuationAware.IsContinuation = UserMessageSpacing.IsUserContinuation(items, index);
            }
        }
    }

    /// <summary>渲染项数上限执行（超出 VisibleItemLimit + FoldStep 触发 — 步长量化推进）：
    /// 折叠提示不占消息配额（先摘后重插）→ 裁最旧至上限-1（预留提示槽）→ 头部挂载折叠提示
    /// → 分界快照随裁剪量左移同步（divider 被裁防御性卸载，快照保留供重建）。
    /// 光标态（MessageActions 激活）跳过 — 裁剪会移位光标选中索引，退出光标态后下一帧补裁。</summary>
    private void EnforceVisibleLimit()
    {
        if (MessageActions is not null || Messages.Count <= VisibleItemLimit + FoldStep)
        {
            return;
        }

        if (_foldedNotice is not null)
        {
            var noticeIndex = Messages.IndexOf(_foldedNotice);
            if (noticeIndex >= 0)
            {
                Messages.RemoveAt(noticeIndex);
            }
        }

        var removed = 0;
        while (Messages.Count > VisibleItemLimit - 1)
        {
            if (ReferenceEquals(Messages.Items[0], _unreadDivider))
            {
                _unreadDivider = null;
            }

            Messages.RemoveAt(0);
            removed++;
        }

        _foldedNotice ??= new FoldedNoticeItem();
        Messages.Insert(0, _foldedNotice);
        if (_unreadBoundaryIndex >= 0)
        {
            _unreadBoundaryIndex = Math.Max(0, _unreadBoundaryIndex - removed);
        }
    }
}
