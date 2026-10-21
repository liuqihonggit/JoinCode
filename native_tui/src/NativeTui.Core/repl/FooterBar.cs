namespace NativeTui.Core.Repl;

/// <summary>
/// 底部状态栏 — 像素级对齐基准 PromptInputFooter（row + space-between + paddingX=2），文案汉化：
/// 行 1 左侧：激活模式 `{symbol} {中文标题} 已开启`（模式色）+dim `（shift+tab 切换）`
/// + busy dim ` · esc 打断`（U+00B7）/ 非 busy 且有 agents 提示 dim ` · {AgentsHint}`；
/// 标准模式（引擎默认 auto）dim `? 查看快捷键`（busy 时 `esc 打断`）；
/// 行 1 右侧 Notifications 登录错误（error 色右对齐）；
/// 行 2 右侧（四源互斥，持续告警最优先）：上下文余量告警 ContextWarning（M8-7 — Critical error /
/// Warning warning 色，持续态压瞬态通知）&gt; 动态通知 <see cref="ReplNotifications.Current"/>（dim）
/// &gt; 静态 <see cref="FooterState.Notification"/>（dim）&gt; TokenWarning（Ok dim / Low warning 色）。
/// 注意：paddingX=2 留白由 REPL 组装层负责（frame.X 从 col 2 起），本组件只渲染内容。
/// </summary>
public sealed class FooterBar
{
    /// <summary>footer 高度：行 2 右侧内容（上下文告警/动态通知/静态通知/Token 警告）非空时 2 行，否则 1 行。</summary>
    public int MeasureHeight(FooterState state) => Row2Text(state) is null ? 1 : 2;

    /// <summary>渲染状态栏（frame 高度须 ≥ MeasureHeight）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme, FooterState state)
    {
        RenderLeftSide(screen, frame with { Height = 1 }, theme, state);
        RenderRightAligned(screen, frame.Y, frame, state.LoginError, theme.Error);
        if (Row2Text(state) is { } row2)
        {
            RenderRightAligned(screen, frame.Y + 1, frame, row2, Row2Color(state, theme));
        }
    }

    /// <summary>行 2 右侧文本 — 四源互斥优先级：上下文告警（持续态最优先 — 通知 12s 超时自然消亡，
    /// 告警持续到余量回升）&gt; 动态通知 &gt; 静态通知 &gt; Token 警告文本。</summary>
    private static string? Row2Text(FooterState state) =>
        state.ContextWarning ?? state.Notifications?.Current ?? state.Notification ?? state.Tokens?.Warning.Text;

    /// <summary>行 2 右侧前景色 — 上下文告警按级别（Critical error / Warning warning），
    /// TokenWarning.Low 用 warning 琥珀（基准 Context low warning 色），其余（通知/Ok）dim（null）。</summary>
    private static Color? Row2Color(FooterState state, Theme theme) =>
        state.ContextWarning is not null ? ContextWarningColor(theme, state.ContextWarningLevel)
        : state.Notifications?.Current is null && state.Notification is null
        && state.Tokens?.Warning.Level == TokenWarningLevel.Low
            ? theme.Warning
            : null;

    /// <summary>告警级别 → 前景色（None 防御性回落 warning 色 — Driver 保证 text null ⟺ level None）。</summary>
    private static Color ContextWarningColor(Theme theme, ContextAlertLevel level) => level switch
    {
        ContextAlertLevel.Critical => theme.Error,
        _ => theme.Warning,
    };

    /// <summary>行 1 左侧：模式指示（标准模式快捷键提示 / 激活模式串 + 尾缀提示）。</summary>
    private static void RenderLeftSide(Screen screen, Rect frame, Theme theme, FooterState state)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        if (state.Mode == PermissionMode.Auto)
        {
            screen.WriteText(frame.Y, frame.X, state.Busy ? "esc 打断" : "? 查看快捷键", dim, frame.Right);
            return;
        }

        var modeStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = PermissionModeInfo.ColorOf(state.Mode, theme) });
        var col = screen.WriteText(frame.Y, frame.X, $"{PermissionModeInfo.Symbol(state.Mode)} {PermissionModeInfo.Title(state.Mode)} 已开启", modeStyle, frame.Right);
        col = screen.WriteText(frame.Y, col, "（shift+tab 切换）", dim, frame.Right);
        var suffix = state.Busy ? "esc 打断" : state.AgentsHint;
        if (suffix is not null)
        {
            col = screen.WriteText(frame.Y, col, Figures.MiddotSeparator, dim, frame.Right);
            screen.WriteText(frame.Y, col, suffix, dim, frame.Right);
        }
    }

    /// <summary>右对齐写入一行文本（foreground 前景色 / null = dim，右端贴 frame.Right）。</summary>
    private static void RenderRightAligned(Screen screen, int row, Rect frame, string? text, Color? foreground)
    {
        if (text is null)
        {
            return;
        }

        var style = screen.Styles.GetOrAdd(foreground is { } color
            ? new TextStyle { Foreground = color }
            : new TextStyle { Dim = true });
        var width = RuneWidth.OfText(text);
        var col = Math.Max(frame.X, frame.Right - width);
        screen.WriteText(row, col, text, style, frame.Right);
    }
}
