namespace NativeTui.Core.Repl;

/// <summary>
/// 底部状态栏 — 像素级对齐基准 PromptInputFooter（row + space-between + paddingX=2）：
/// 行 1 左侧 PromptInputFooterLeftSide：激活模式 `{symbol} {title} on`（模式色）+ dim ` (shift+tab to cycle)`
/// + busy dim ` · esc to interrupt`（U+00B7）/ 非 busy 且有 agents 提示 dim ` · {AgentsHint}`（v2.1.303 实测）；
/// 默认模式 dim `? for shortcuts`（busy 时 `esc to interrupt`）；
/// 行 1 右侧 Notifications 登录错误（error 色右对齐）；
/// 行 2 右侧通知（如 effort `◐ medium · /effort`，dim 右对齐）。
/// 注意：paddingX=2 留白由 REPL 组装层负责（frame.X 从 col 2 起），本组件只渲染内容。
/// </summary>
public sealed class FooterBar
{
    /// <summary>footer 高度：Notification 非空时 2 行（行 2 右对齐通知），否则 1 行。</summary>
    public int MeasureHeight(FooterState state) => state.Notification is null ? 1 : 2;

    /// <summary>渲染状态栏（frame 高度须 ≥ MeasureHeight）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme, FooterState state)
    {
        RenderLeftSide(screen, frame with { Height = 1 }, theme, state);
        RenderRightAligned(screen, frame.Y, frame, theme, state.LoginError, error: true);
        if (state.Notification is not null)
        {
            RenderRightAligned(screen, frame.Y + 1, frame, theme, state.Notification, error: false);
        }
    }

    /// <summary>行 1 左侧：模式指示（默认模式快捷键提示 / 激活模式串 + 尾缀提示）。</summary>
    private static void RenderLeftSide(Screen screen, Rect frame, Theme theme, FooterState state)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        if (state.Mode == PermissionMode.Default)
        {
            screen.WriteText(frame.Y, frame.X, state.Busy ? "esc to interrupt" : "? for shortcuts", dim, frame.Right);
            return;
        }

        var modeStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = PermissionModeInfo.ColorOf(state.Mode, theme) });
        var col = screen.WriteText(frame.Y, frame.X, $"{PermissionModeInfo.Symbol(state.Mode)} {PermissionModeInfo.Title(state.Mode)} on", modeStyle, frame.Right);
        col = screen.WriteText(frame.Y, col, " (shift+tab to cycle)", dim, frame.Right);
        var suffix = state.Busy ? "esc to interrupt" : state.AgentsHint;
        if (suffix is not null)
        {
            col = screen.WriteText(frame.Y, col, Figures.MiddotSeparator, dim, frame.Right);
            screen.WriteText(frame.Y, col, suffix, dim, frame.Right);
        }
    }

    /// <summary>右对齐写入一行文本（error 色或 dim，右端贴 frame.Right）。</summary>
    private static void RenderRightAligned(Screen screen, int row, Rect frame, Theme theme, string? text, bool error)
    {
        if (text is null)
        {
            return;
        }

        var style = error
            ? screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Error })
            : screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var width = RuneWidth.OfText(text);
        var col = Math.Max(frame.X, frame.Right - width);
        screen.WriteText(row, col, text, style, frame.Right);
    }
}
