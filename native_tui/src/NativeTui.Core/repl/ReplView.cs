namespace NativeTui.Core.Repl;

/// <summary>
/// REPL 整屏视图 — 对齐基准 REPL.tsx bottom slot 布局（自底向上）：
/// footer 1-2 行（paddingX=2，通知非空时 2 行）+ 输入底边线 1 行 + 输入文本 1 行
/// + 输入顶边线 1 行（基准 PromptInput 上下全宽 ─ 分隔线）+ 间隔 1 行（输入框 marginTop=1），
/// 其余全部给消息滚动区（sticky 钉底）。
/// spinner 行（输入框上方，基准 scrollable 区尾部）由滚动区尾部 SpinnerItem 承载。
/// </summary>
public sealed class ReplView
{
    private readonly FooterBar _footer = new();
    private readonly PromptInputBox _inputBox = new();

    /// <summary>消息滚动区（sticky 钉底）。</summary>
    public ScrollBox Messages { get; } = new(stickyScroll: true);

    /// <summary>输入框文本。</summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>光标文本内偏移（0 = 首字符之前）。</summary>
    public int CursorCol { get; set; }

    /// <summary>footer 状态（模式/busy/agents 提示/登录错误/通知）。</summary>
    public FooterState Footer { get; set; } = new();

    /// <summary>渲染整屏（消息区 → 输入框 → footer）。</summary>
    public void Render(Screen screen, Theme theme)
    {
        var footerHeight = _footer.MeasureHeight(Footer);
        var footerFrame = new Rect(2, screen.Rows - footerHeight, Math.Max(0, screen.Cols - 4), footerHeight);
        _footer.Render(screen, footerFrame, theme, Footer);

        var inputFrame = new Rect(0, screen.Rows - footerHeight - 3, screen.Cols, 3);
        _inputBox.Render(screen, inputFrame, theme, Input, CursorCol, Footer.Busy);

        var messagesHeight = Math.Max(0, screen.Rows - footerHeight - 4);
        var messagesFrame = new Rect(0, 0, screen.Cols, messagesHeight);
        Messages.Render(screen, messagesFrame, theme);
    }
}
