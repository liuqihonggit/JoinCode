namespace NativeTui.Core.Repl;

/// <summary>
/// 空闲返回动作（基准 IdleReturnDialog.tsx:7 type IdleReturnAction = 'continue'|'clear'|'dismiss'|'never'）：
/// Continue = 继续原会话提交；Clear = 作为新会话发送（清空上下文）；Never = 不再询问（会话级持久）；
/// Dismiss = Esc 取消（恢复输入框文本不提交 — 基准 REPL.tsx:4822-4825 onDone('dismiss') → setInputValue 语义）。
/// </summary>
public enum IdleReturnAction : byte
{
    /// <summary>继续此对话（原样重发暂存提交 — 基准 label "Continue this conversation"）。</summary>
    Continue = 0,

    /// <summary>作为新会话发送（清空上下文后重发 — 基准 label "Send message as a new conversation"）。</summary>
    Clear = 1,

    /// <summary>不再询问（会话级持久标志 — 基准 label "Don't ask me again"）。</summary>
    Never = 2,

    /// <summary>Esc 取消（恢复输入框文本，丢弃本次提交动作）。</summary>
    Dismiss = 3,
}

/// <summary>
/// 空闲返回对话框（TASK066 板块 D D1 — 基准 components/IdleReturnDialog.tsx：用户离开超阈值后
/// 首次提交时拦截询问去留，避免冷缓存大上下文继续烧 token）：构造注入 idleMinutes +
/// totalInputTokens（快照纯显示 — 宿主挂载前拍平，组件零查询零回调零时钟）→ ↑↓ clamp 导航 →
/// enter 确认（SelectedAction 契约属性暴露选中动作）/ esc 取消（Dismiss）；动作执行（重发/
/// 新会话/恢复输入框）属宿主接线（纯 UI 组件 — 契约属性轮询，对齐 QuickOpenDialog 形态）。
/// 渲染对齐 QuickOpenDialog 形态学（permission 色边框 + 标题 + dim 提示行 + 选项列表焦点行
/// ❯ suggestion 高亮 + footer Byline）：margin + 边框 + 标题 + 提示行 + 空行 + 三选项 + 空行 + footer。
/// </summary>
public sealed class IdleReturnDialog : IScrollItem
{
    /// <summary>三选项（基准 IdleReturnDialog.tsx:56-79 Select options 原文顺序 continue/clear/never）。</summary>
    private static readonly (IdleReturnAction Action, string Label)[] Options =
    [
        (IdleReturnAction.Continue, "继续此对话"),
        (IdleReturnAction.Clear, "作为新会话发送"),
        (IdleReturnAction.Never, "不再询问"),
    ];

    /// <summary>提示行文案（基准 IdleReturnDialog.tsx:49 Dialog body 首行
    /// "If this is a new task, clearing context will save usage and be faster."）。</summary>
    private const string HintText = "如果是新任务，清空上下文将节省用量并更快。";

    /// <summary>footer Byline（对齐 QuickOpenDialog footer 形态 — esc 语义为返回输入框恢复文本）。</summary>
    private static readonly string FooterText =
        $"{Figures.UpArrow}{Figures.DownArrow} 导航{Figures.MiddotSeparator}enter 确认{Figures.MiddotSeparator}esc 返回";

    /// <summary>固定行数 = margin 1 + 边框 1 + 标题 1 + 提示 1 + 空行 1 + 空行 1 + footer 1。</summary>
    private const int FixedRows = 7;

    private readonly double _idleMinutes;
    private readonly long _totalInputTokens;
    private int _selectedIndex;
    private bool _closed;
    private IdleReturnAction? _selectedAction;

    /// <summary>创建空闲返回对话框 — idleMinutes/totalInputTokens 为宿主挂载前拍平的快照
    ///（基准 REPL.tsx:3332 setIdleReturnPending({input, idleMinutes}) + :4813 totalInputTokens={getTotalInputTokens()}）。</summary>
    /// <param name="idleMinutes">空闲时长（分钟 — 回合完成时间戳差值换算）。</param>
    /// <param name="totalInputTokens">累计输入 token（会话累计 — 对话大小展示依据）。</param>
    public IdleReturnDialog(double idleMinutes, long totalInputTokens)
    {
        _idleMinutes = idleMinutes;
        _totalInputTokens = totalInputTokens;
    }

    /// <summary>选中索引（clamp 于 [0, 选项数-1]；初始 0 = 焦点在"继续此对话"）。</summary>
    public int SelectedIndex => _selectedIndex;

    /// <summary>焦点选项（三选项 Enter 之一；Dismiss 为 Esc 专属）。</summary>
    public IdleReturnAction FocusedAction => Options[_selectedIndex].Action;

    /// <summary>确认动作（契约属性 — enter 选中三选项之一 / esc 置 Dismiss；null = 未关闭未决）。</summary>
    public IdleReturnAction? SelectedAction => _selectedAction;

    /// <summary>是否已关闭（enter 确认或 esc 取消 — 宿主消费后移除对话框项）。</summary>
    public bool IsClosed => _closed;

    /// <summary>标题全文（internal — 测试断言面；基准 IdleReturnDialog.tsx:38
    /// `You've been away {时长} and this conversation is {tokens} tokens.` 汉化形态）。</summary>
    internal string FormattedTitle =>
        $"你已离开 {FormatIdleDuration(_idleMinutes)}，此对话已 {MessageRenderer.FormatTokens((int)_totalInputTokens)} tokens。";

    /// <summary>
    /// 键位路由（模态契约 — 对话框激活期间全部吞掉返回 true，对齐 QuickOpenDialog.TryHandleKey 模态语义）：
    /// ↑ 上移 / ↓ 下移（clamp 不环绕）/ enter 确认焦点选项 / esc 取消（Dismiss）；其余键（字符/Tab/
    /// Ctrl 组合等）静默吞掉。已关闭返回 false（宿主不应再路由 — 防御契约）。
    /// </summary>
    /// <param name="key">按键事件。</param>
    /// <returns>true = 已消费（模态吞掉）；false = 对话框已关闭（宿主接管后续动作）。</returns>
    public bool TryHandleKey(KeyPress key)
    {
        if (_closed)
        {
            return false;
        }

        if (key.Kind == KeyKind.Escape)
        {
            Select(IdleReturnAction.Dismiss);
            return true;
        }

        if (key.Kind == KeyKind.Return)
        {
            Select(FocusedAction);
            return true;
        }

        if (key.Kind == KeyKind.UpArrow)
        {
            MoveFocus(-1);
            return true;
        }

        if (key.Kind == KeyKind.DownArrow)
        {
            MoveFocus(1);
            return true;
        }

        return true;
    }

    /// <summary>内容高度 = 固定 7 行 + 三选项 3 行 = 10；关闭后 0。</summary>
    public int MeasureHeight(int width) => _closed ? 0 : FixedRows + Options.Length;

    /// <summary>渲染：按固定行序绘制（关闭后不画 — 宿主消费 IsClosed 后移除对话框项）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (_closed)
        {
            return;
        }

        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderModelTitle(screen, RowFrame(frame, row++), theme, FormattedTitle);
        RenderHintRow(screen, RowFrame(frame, row++));
        row++;
        for (var i = 0; i < Options.Length; i++)
        {
            RenderOptionRow(screen, RowFrame(frame, row++), theme, Options[i].Label, i == _selectedIndex);
        }

        row++;
        MessageRenderer.RenderModelShortcuts(screen, RowFrame(frame, row), theme, FooterText);
    }

    /// <summary>
    /// 空闲时长格式化（internal — 测试断言面；基准 IdleReturnDialog.tsx:104-117 formatIdleDuration 逐分支）：
    /// &lt;1m → "&amp;lt; 1m"；&lt;60m → 整数分钟 "Nm"；整小时 → "Nh"；否则 "Nh Nm"。
    /// </summary>
    /// <param name="minutes">空闲时长（分钟 — 浮点由宿主差值换算）。</param>
    /// <returns>人读时长文本。</returns>
    internal static string FormatIdleDuration(double minutes)
    {
        if (minutes < 1)
        {
            return "< 1m";
        }

        if (minutes < 60)
        {
            return $"{Math.Floor(minutes)}m";
        }

        var hours = Math.Floor(minutes / 60);
        var remainingMinutes = Math.Floor(minutes % 60);
        if (remainingMinutes == 0)
        {
            return $"{hours}h";
        }

        return $"{hours}h {remainingMinutes}m";
    }

    /// <summary>焦点移动 — clamp 不环绕（列表语义，对齐 QuickOpenDialog.MoveFocus；三选项无滚动窗口）。</summary>
    private void MoveFocus(int delta) =>
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, Options.Length - 1);

    /// <summary>确认/取消 — 记录选中动作并置关闭。</summary>
    private void Select(IdleReturnAction action)
    {
        _selectedAction = action;
        _closed = true;
    }

    /// <summary>提示行渲染 — 1 空格缩进 dim（基准 Dialog body 首行副文案）。</summary>
    private static void RenderHintRow(Screen screen, Rect frame)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $" {HintText}", dim, frame.Right);
    }

    /// <summary>选项行渲染（对齐 QuickOpenDialog.RenderResultRow 形态，无序号）：
    /// 焦点 = ❯ suggestion + 空格 + 选项文案 suggestion 色；非焦点 = 2 空格缩进 + 默认样式。</summary>
    private static void RenderOptionRow(Screen screen, Rect frame, Theme theme, string label, bool focused)
    {
        if (focused)
        {
            var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion });
            screen[frame.Y, frame.X] = new Cell(new Rune('❯'), suggestion, CellWidth.Default);
            screen.WriteText(frame.Y, frame.X + 1, " ", suggestion, frame.Right);
            screen.WriteText(frame.Y, frame.X + 2, label, suggestion, frame.Right);
            return;
        }

        screen.WriteText(frame.Y, frame.X, "  ", 0, frame.Right);
        screen.WriteText(frame.Y, frame.X + 2, label, 0, frame.Right);
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
