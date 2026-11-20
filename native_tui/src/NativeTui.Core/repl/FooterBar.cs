namespace NativeTui.Core.Repl;

/// <summary>
/// 底部状态栏 — 像素级对齐基准 PromptInputFooter（row + space-between + paddingX=2），文案汉化：
/// 行 1 左侧：激活模式 `{symbol} {中文标题} 已开启`（模式色）+dim `（shift+tab 切换）`
/// + busy dim ` · esc 打断`（U+00B7）/ 非 busy 且有 agents 提示 dim ` · {AgentsHint}`；
/// 标准模式（引擎默认 auto）dim `? 查看快捷键`（busy 时 `esc 打断`）；
/// 行 1 右侧 Notifications 登录错误（error 色右对齐）；
/// 行 2 起右侧为**多行堆叠列**（TASK069 N2 — 对齐基准 Notifications.tsx:203 `flexDirection="column"`：
/// 各来源各自成行并行显示，不再单槽互斥），自上而下：自定义状态行 StatusLine（用户显式配置最高）
/// &gt; 上下文余量告警 ContextWarning（M8-7 — Critical error / Warning warning）&gt; 动态通知
/// <see cref="ReplNotifications.Current"/>（dim）&gt; 静态 <see cref="FooterState.Notification"/>（dim）
/// &gt; TokenWarning（Ok dim / Low warning 色）。高度随活跃来源数增长。
/// 注意：paddingX=2 留白由 REPL 组装层负责（frame.X 从 col 2 起），本组件只渲染内容。
/// </summary>
public sealed class FooterBar
{
    /// <summary>窄屏阈值（TASK069 N3 — 基准 Notifications.tsx:175 columns&lt;80 布局切换）。</summary>
    public const int NarrowScreenColumns = 80;

    /// <summary>footer 高度（列宽无关 — 1 行模式行 + 右列堆叠行数）。</summary>
    public int MeasureHeight(FooterState state) => 1 + RightRowCount(state);

    /// <summary>footer 高度（窄屏感知 — TASK069 N3）：窄屏（&lt;80 列）时 LoginError 独立行堆叠
    ///（对齐基准右列上下堆叠 — 左右同行互挤覆盖消除），高度 +1。</summary>
    public int MeasureHeight(FooterState state, int cols) =>
        MeasureHeight(state) + (IsNarrowLoginRow(state, cols) ? 1 : 0);

    /// <summary>窄屏且存在登录错误 → LoginError 单独占一行（不做右对齐同行渲染）。</summary>
    private static bool IsNarrowLoginRow(FooterState state, int cols) =>
        cols < NarrowScreenColumns && state.LoginError is not null;

    /// <summary>渲染状态栏（frame 高度须 ≥ MeasureHeight）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme, FooterState state)
    {
        RenderLeftSide(screen, frame with { Height = 1 }, theme, state);
        var rowOffset = 0;
        if (IsNarrowLoginRow(state, frame.Width))
        {
            // TASK069 N3 — 窄屏 LoginError 独立行（不再行 1 右对齐与模式串互挤覆盖）
            rowOffset = 1;
            RenderRightAligned(screen, frame.Y + rowOffset, frame, state.LoginError, theme.Error);
        }
        else
        {
            RenderRightAligned(screen, frame.Y, frame, state.LoginError, theme.Error);
        }

        // TASK069 N2 — 右列多行堆叠（基准 Notifications.tsx column）：每个活跃来源各自一行，互不覆盖
        var row = frame.Y + rowOffset + 1;
        foreach (var (text, foreground) in RightRows(state, theme))
        {
            RenderRightAligned(screen, row, frame, text, foreground);
            row++;
        }
    }

    /// <summary>右列堆叠行数（高度用 — 与 <see cref="RightRows"/> 同源判定）。</summary>
    private static int RightRowCount(FooterState state) =>
        (IsBlank(state.StatusLine) ? 0 : 1)
        + (IsBlank(state.ContextWarning) ? 0 : 1)
        + (IsBlank(state.Notifications?.Current) ? 0 : 1)
        + (IsBlank(state.Notification) ? 0 : 1)
        + (IsBlank(state.Tokens?.Warning.Text) ? 0 : 1);

    /// <summary>空/缺省来源不占行（null 或空串一视同仁）。</summary>
    private static bool IsBlank(string? text) => text is not { Length: > 0 };

    /// <summary>右列堆叠行（自上而下按优先级 — TASK069 N2 多源并行显示）：自定义状态行
    /// &gt; 上下文告警（持续态 — 通知 12s 超时自然消亡，告警持续到余量回升）&gt; 动态通知
    /// &gt; 静态通知 &gt; Token 警告文本。</summary>
    private static IReadOnlyList<(string Text, Color? Foreground)> RightRows(FooterState state, Theme theme)
    {
        var rows = new List<(string Text, Color? Foreground)>(5);
        if (state.StatusLine is { Length: > 0 } statusLine)
        {
            rows.Add((statusLine, null));
        }

        if (state.ContextWarning is { Length: > 0 } contextWarning)
        {
            rows.Add((contextWarning, ContextWarningColor(theme, state.ContextWarningLevel)));
        }

        if (state.Notifications?.Current is { Length: > 0 } current)
        {
            rows.Add((current, null));
        }

        if (state.Notification is { Length: > 0 } notification)
        {
            rows.Add((notification, null));
        }

        if (state.Tokens?.Warning.Text is { Length: > 0 } tokenWarning)
        {
            // 独立行后 TokenWarning 自身决定色（Low 琥珀 / Ok dim）— 不再受上方来源挤占影响
            rows.Add((tokenWarning, state.Tokens?.Warning.Level == TokenWarningLevel.Low ? theme.Warning : null));
        }

        return rows;
    }

    /// <summary>告警级别 → 前景色（None 防御性回落 warning 色 — Driver 保证 text null ⟺ level None）。</summary>
    private static Color ContextWarningColor(Theme theme, ContextAlertLevel level) => level switch
    {
        ContextAlertLevel.Critical => theme.Error,
        _ => theme.Warning,
    };

    /// <summary>行 1 左侧：vim INSERT 指示（优先前置）+ 模式指示（标准模式快捷键提示 / 激活模式串 + 尾缀提示）。</summary>
    private static void RenderLeftSide(Screen screen, Rect frame, Theme theme, FooterState state)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // TASK066 板块 E 连线 E3 — vim INSERT 指示（基准 PromptInputFooterLeftSide.tsx:170/191/216）：
        // isVimModeEnabled() && vimMode === "INSERT" 时 dim "-- INSERT --" 置于左侧最前
        //（gap=1 空格与模式指示并列 — ModeIndicator 本体照常渲染）；NORMAL 态 VimMode null
        // 不渲染（零变化）；showVim 抑制标准模式 hint（:197 !suppressHint && !showVim —
        // "? 查看快捷键"/"esc 打断" 不显示 — INSERT 态 Esc 语义已是切模式，打断提示反而误导）
        var col = frame.X;
        if (state.VimMode is { } vimIndicator)
        {
            col = screen.WriteText(frame.Y, col, vimIndicator, dim, frame.Right);
            col = screen.WriteText(frame.Y, col, " ", dim, frame.Right);
        }

        if (state.Mode == PermissionMode.Auto)
        {
            if (state.VimMode is null)
            {
                screen.WriteText(frame.Y, frame.X, state.Busy ? "esc 打断" : "? 查看快捷键", dim, frame.Right);
            }

            return;
        }

        var modeStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = PermissionModeInfo.ColorOf(state.Mode, theme) });
        col = screen.WriteText(frame.Y, col, $"{PermissionModeInfo.Symbol(state.Mode)} {PermissionModeInfo.Title(state.Mode)} 已开启", modeStyle, frame.Right);
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
