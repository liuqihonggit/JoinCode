namespace NativeTui.Core.Repl;

/// <summary>
/// REPL 整屏视图 — 对齐基准 REPL.tsx bottom slot 布局（自底向上）：
/// footer 1 行（paddingX=2，基准 PromptInputFooter）+ 输入底边线 1 行 + 输入文本 1 行
/// + 输入顶边线 1 行（基准 PromptInput 上下全宽 ─ 分隔线）+ 间隔 1 行（输入框 marginTop=1），
/// 其余全部给消息滚动区（sticky 钉底）。
/// spinner 行（输入框上方，基准 scrollable 区尾部）属 M5 动画系统，由滚动区尾部 SpinnerItem 承载。
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

    /// <summary>是否忙（输入 ❯ dim + footer 追加 esc to interrupt）。</summary>
    public bool Busy { get; set; }

    /// <summary>权限模式（footer 模式指示）。</summary>
    public PermissionMode Mode { get; set; }

    /// <summary>渲染整屏（消息区 → 输入框 → footer）。</summary>
    public void Render(Screen screen, Theme theme)
    {
        var footerFrame = new Rect(2, screen.Rows - 1, Math.Max(0, screen.Cols - 4), 1);
        _footer.Render(screen, footerFrame, theme, Mode, Busy);

        var inputFrame = new Rect(0, screen.Rows - 4, screen.Cols, 3);
        _inputBox.Render(screen, inputFrame, theme, Input, CursorCol, Busy);

        var messagesFrame = new Rect(0, 0, screen.Cols, screen.Rows - 5);
        Messages.Render(screen, messagesFrame, theme);
    }
}
