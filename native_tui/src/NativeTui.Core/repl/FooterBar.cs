namespace NativeTui.Core.Repl;

/// <summary>
/// 底部状态栏 — 像素级对齐基准 PromptInputFooter/PromptInputFooterLeftSide：
/// 激活模式：`{symbol} {title} on`（模式色）+ dim ` (shift+tab to cycle)`；
/// busy 追加 dim ` · esc to interrupt`（分隔符 U+00B7 middot，非 ∙）；
/// 默认模式：dim `? for shortcuts`（busy 时 `esc to interrupt`）。
/// 注意：基准 PromptInputFooter 外层的 paddingX=2 留白由 REPL 组装层负责，本组件只渲染内容行。
/// spinner 属于输入框上方的独立行组件（基准 REPL.tsx scrollable 区），不在 footer。
/// </summary>
public sealed class FooterBar
{
    /// <summary>渲染模式指示行（单行）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme, PermissionMode mode, bool busy = false)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        if (mode == PermissionMode.Default)
        {
            screen.WriteText(frame.Y, frame.X, busy ? "esc to interrupt" : "? for shortcuts", dim, frame.Right);
            return;
        }

        var modeStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = PermissionModeInfo.ColorOf(mode, theme) });
        var col = screen.WriteText(frame.Y, frame.X, $"{PermissionModeInfo.Symbol(mode)} {PermissionModeInfo.Title(mode)} on", modeStyle, frame.Right);
        col = screen.WriteText(frame.Y, col, " (shift+tab to cycle)", dim, frame.Right);
        if (busy)
        {
            col = screen.WriteText(frame.Y, col, Figures.MiddotSeparator, dim, frame.Right);
            screen.WriteText(frame.Y, col, "esc to interrupt", dim, frame.Right);
        }
    }
}
