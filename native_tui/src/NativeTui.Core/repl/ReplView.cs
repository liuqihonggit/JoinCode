namespace NativeTui.Core.Repl;

/// <summary>
/// REPL 整屏视图 — 对齐基准 REPL.tsx bottom slot 布局（自底向上）：
/// footer 1-2 行（paddingX=2，通知非空时 2 行）+ 建议区 0-6 行（斜杠补全激活时，基准 PromptInputFooterSuggestions）
/// + 输入底边线 1 行 + 输入文本 1 行 + 输入顶边线 1 行（基准 PromptInput 上下全宽 ─ 分隔线）
/// + 间隔 1 行（输入框 marginTop=1），其余全部给消息滚动区（sticky 钉底）。
/// spinner 行（输入框上方，基准 scrollable 区尾部）由滚动区尾部 SpinnerItem 承载。
/// </summary>
public sealed class ReplView
{
    private readonly FooterBar _footer = new();
    private readonly PromptInputBox _inputBox = new();
    private readonly SuggestionBox _suggestionBox = new();

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

    /// <summary>建议区高度（0 = 未激活）。</summary>
    private int SuggestionHeight => _suggestionBox.MeasureHeight(Suggestions?.Count ?? 0);

    /// <summary>排队面板高度（0 = 无排队）。</summary>
    private int QueueHeight => QueuedInputs.Count;

    /// <summary>
    /// 内容总高度（inline 模式建屏用）— 消息全量高度 + 间隔 1 + 输入区 3 + 排队面板 + 建议区 + footer 高度；
    /// 屏高取此值时消息区恰好全量展示（无滚动窗口），历史自然滚入终端 scrollback。
    /// </summary>
    public int MeasureContentHeight(int cols) =>
        Messages.MeasureContentHeight(cols) + 4 + QueueHeight + SuggestionHeight + _footer.MeasureHeight(Footer);

    /// <summary>inline 模式尾部动态区行数（间隔 1 + 输入区 3 + 排队面板 + 建议区 + footer 高度）— 退出清理用。</summary>
    public int TailHeight => 4 + QueueHeight + SuggestionHeight + _footer.MeasureHeight(Footer);

    /// <summary>
    /// 输入光标内容坐标（inline 模式帧尾光标位）— 行 = 输入文本行（内容底上方 footer+建议区+2），
    /// 列 = PromptWidth + 显示宽度（修复：中文宽字符 — char 偏移换算显示列，钳制行宽，右缘即 pending wrap 状态）。
    /// </summary>
    public CursorPos InputCursorPos(int contentRows, int cols, int cursorCol)
    {
        var footerHeight = _footer.MeasureHeight(Footer);
        var displayCol = PromptInputBox.MeasureDisplayWidth(Input.AsSpan(0, Math.Min(cursorCol, Input.Length)));
        return new CursorPos(Math.Min(cols, PromptInputBox.PromptWidth + displayCol), contentRows - footerHeight - SuggestionHeight - 2);
    }

    /// <summary>渲染整屏（消息区 → 排队面板 → 输入框 → 建议区 → footer）。</summary>
    public void Render(Screen screen, Theme theme)
    {
        var footerHeight = _footer.MeasureHeight(Footer);
        var suggestionHeight = SuggestionHeight;
        var queueHeight = QueueHeight;
        var footerFrame = new Rect(2, screen.Rows - footerHeight, Math.Max(0, screen.Cols - 4), footerHeight);
        _footer.Render(screen, footerFrame, theme, Footer);

        if (suggestionHeight > 0 && Suggestions is { Count: > 0 })
        {
            var suggestionFrame = new Rect(0, screen.Rows - footerHeight - suggestionHeight, screen.Cols, suggestionHeight);
            _suggestionBox.Render(screen, suggestionFrame, theme, Suggestions, SuggestionIndex, SuggestionNameColumnWidth, SuggestionArgumentMode);
        }

        var inputFrame = new Rect(0, screen.Rows - footerHeight - suggestionHeight - 3, screen.Cols, 3);
        _inputBox.Render(screen, inputFrame, theme, Input, CursorCol, Footer.Busy, GhostSuffix);

        if (queueHeight > 0)
        {
            RenderQueuePanel(screen, new Rect(0, inputFrame.Y - queueHeight, screen.Cols, queueHeight), theme);
        }

        var messagesHeight = Math.Max(0, screen.Rows - footerHeight - suggestionHeight - queueHeight - 4);
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
}
