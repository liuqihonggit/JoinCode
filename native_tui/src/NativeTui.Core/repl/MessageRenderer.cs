namespace NativeTui.Core.Repl;

/// <summary>
/// 消息渲染器 — 像素级对齐基准消息组件（M5 REPL 主界面）：
/// RenderUser → UserPromptMessage（整行灰底 + ❯ subtle 色 + 正文 text 色 + paddingRight=1）；
/// RenderAssistant → AssistantTextMessage（● text 色 + minWidth 2 + 正文）；
/// RenderToolCall → AssistantToolUseMessage（● + bold 工具名 + 默认样式 (参数摘要)）；
/// RenderToolResult → MessageResponse（"  ⎿  " 前缀整体 dim + 默认正文）。
/// </summary>
public static class MessageRenderer
{
    /// <summary>
    /// 渲染用户消息：灰底满行宽（Box stretch），❯ 前缀 subtle 色（无 paddingLeft），
    /// 正文 text 色，paddingRight=1（正文区右边界 = frame.Right - 1）。
    /// </summary>
    public static void RenderUser(Screen screen, Rect frame, Theme theme, string text)
    {
        var background = theme.UserMessageBackground;
        var fill = screen.Styles.GetOrAdd(new TextStyle { Background = background });
        for (var col = frame.X; col < frame.Right; col++)
        {
            screen[frame.Y, col] = new Cell(new Rune(' '), fill, CellWidth.Default);
        }

        var pointer = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Subtle, Background = background });
        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text, Background = background });
        var textRight = frame.Right - 1;
        screen.WriteText(frame.Y, frame.X, $"{Figures.Pointer} ", pointer, textRight);
        screen.WriteText(frame.Y, frame.X + 2, text, body, textRight);
    }

    /// <summary>渲染助手文本消息：● + 空格（minWidth 2）+ 正文，全部 text 色。</summary>
    public static void RenderAssistant(Screen screen, Rect frame, Theme theme, string text)
    {
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        screen.WriteText(frame.Y, frame.X, $"{Figures.MessageBullet} ", style, frame.Right);
        screen.WriteText(frame.Y, frame.X + 2, text, style, frame.Right);
    }

    /// <summary>渲染工具调用消息：● + 空格 + bold 工具名 + 默认样式 (参数摘要)，名字与摘要间无间隔。</summary>
    public static void RenderToolCall(Screen screen, Rect frame, Theme theme, string toolName, string args)
    {
        var bullet = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(frame.Y, frame.X, $"{Figures.MessageBullet} ", bullet, frame.Right);
        var col = screen.WriteText(frame.Y, frame.X + 2, toolName, bold, frame.Right);
        screen.WriteText(frame.Y, col, $"({args})", 0, frame.Right);
    }

    /// <summary>渲染工具结果消息："  ⎿  " 前缀整体 dim（单一 Text span），正文默认样式。</summary>
    public static void RenderToolResult(Screen screen, Rect frame, Theme theme, string text)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, Figures.ToolResultIndent, dim, frame.Right);
        screen.WriteText(frame.Y, frame.X + Figures.ToolResultIndent.Length, text, 0, frame.Right);
    }

    /// <summary>渲染错误消息：● + 空格 + 正文，全部 theme.Error 红色（bridge error 事件）。</summary>
    public static void RenderError(Screen screen, Rect frame, Theme theme, string text)
    {
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Error });
        screen.WriteText(frame.Y, frame.X, $"{Figures.MessageBullet} ", style, frame.Right);
        screen.WriteText(frame.Y, frame.X + 2, text, style, frame.Right);
    }

    /// <summary>渲染 diff hunk 头："    @@ … @@"（4 空格缩进，整行 dim 弱化）。</summary>
    public static void RenderDiffHeader(Screen screen, Rect frame, Theme theme, string header)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"    {header}", dim, frame.Right);
    }

    /// <summary>
    /// 渲染单个 diff 行："    " + 前缀字符 + 正文（4 空格缩进对齐青蛙TUI 先例）：
    /// added 整行 DiffAddedWord 前景 / removed 整行 DiffRemovedWord 前景 / context 整行 dim。
    /// </summary>
    public static void RenderDiffLine(Screen screen, Rect frame, Theme theme, DiffLine line)
    {
        var style = line.Kind switch
        {
            DiffLineKind.Added => screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.DiffAddedWord }),
            DiffLineKind.Removed => screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.DiffRemovedWord }),
            _ => screen.Styles.GetOrAdd(new TextStyle { Dim = true }),
        };
        var prefix = line.Kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "-",
            _ => " ",
        };
        screen.WriteText(frame.Y, frame.X, $"    {prefix}{line.Content}", style, frame.Right);
    }

    /// <summary>渲染折叠提示："    … +N lines (Ctrl+O)"（dim；Ctrl+O 展开交互为 M7 后续项）。</summary>
    public static void RenderDiffCollapseHint(Screen screen, Rect frame, Theme theme, int hiddenLines)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"    … +{hiddenLines} lines (Ctrl+O)", dim, frame.Right);
    }

    /// <summary>渲染权限对话框顶边框 — 整行 ─ 前景 permission 色（基准 borderStyle=round 无角，仅 top，全宽）。</summary>
    public static void RenderPermissionBorder(Screen screen, Rect frame, Theme theme)
    {
        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Permission });
        for (var col = frame.X; col < frame.Right; col++)
        {
            screen[frame.Y, col] = new Cell(new Rune('─'), border, CellWidth.Default);
        }
    }

    /// <summary>渲染权限对话框标题行 — 1 空格缩进 + bold permission 色（基准 PermissionRequestTitle）。</summary>
    public static void RenderPermissionTitle(Screen screen, Rect frame, Theme theme, string title)
    {
        var style = screen.Styles.GetOrAdd(new TextStyle { Bold = true, Foreground = theme.Permission });
        screen.WriteText(frame.Y, frame.X, $" {title}", style, frame.Right);
    }

    /// <summary>渲染权限工具行 — 3 空格缩进（外 1 + 内 2 paddingX 累计）+ bold 工具名 + 默认样式 (描述参数)。</summary>
    public static void RenderPermissionToolLine(Screen screen, Rect frame, Theme theme, string toolName, string description)
    {
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(frame.Y, frame.X, "   ", 0, frame.Right);
        var col = screen.WriteText(frame.Y, frame.X + 3, toolName, bold, frame.Right);
        screen.WriteText(frame.Y, col, $"({description})", 0, frame.Right);
    }

    /// <summary>渲染权限描述行 — 3 空格缩进整行 dim。</summary>
    public static void RenderPermissionDescription(Screen screen, Rect frame, Theme theme, string description)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"   {description}", dim, frame.Right);
    }

    /// <summary>渲染权限问题行 — 1 空格缩进默认样式（基准 PermissionPrompt 固定文案）。</summary>
    public static void RenderPermissionQuestion(Screen screen, Rect frame, Theme theme)
    {
        screen.WriteText(frame.Y, frame.X, " Do you want to proceed?", 0, frame.Right);
    }

    /// <summary>
    /// 渲染权限选项行（基准 CustomSelect compact + ListItem）：
    /// 焦点 = ❯ suggestion + gap 1 + "N. " dim + label suggestion；非焦点 = 空格 indicator + gap 1 + "N. " dim + label 默认。
    /// </summary>
    public static void RenderPermissionOption(Screen screen, Rect frame, Theme theme, int index, string label, bool focused)
    {
        if (focused)
        {
            var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion });
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            screen[frame.Y, frame.X] = new Cell(new Rune('❯'), suggestion, CellWidth.Default);
            screen.WriteText(frame.Y, frame.X + 1, $" ", dim, frame.Right);
            screen.WriteText(frame.Y, frame.X + 2, $"{index + 1}. ", dim, frame.Right);
            screen.WriteText(frame.Y, frame.X + 5, label, suggestion, frame.Right);
            return;
        }

        var plain = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"  {index + 1}. ", plain, frame.Right);
        screen.WriteText(frame.Y, frame.X + 5, label, 0, frame.Right);
    }

    /// <summary>渲染权限取消提示行 — 1 空格缩进 dim（基准 "Esc to cancel"）。</summary>
    public static void RenderPermissionEscHint(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, " Esc to cancel", dim, frame.Right);
    }

    /// <summary>
    /// 渲染 Markdown 物理行（M7-4）— 从指定列起逐段写，段样式经 MapMarkdownStyle 映射
    ///（Code → permission 色前景，对齐基准 codespan color('permission')；修饰开关直转 TextStyle）。
    /// </summary>
    public static void RenderMarkdownLine(Screen screen, Rect frame, Theme theme, int startCol, IReadOnlyList<MarkdownSegment> segments)
    {
        var col = frame.X + startCol;
        foreach (var segment in segments)
        {
            var style = screen.Styles.GetOrAdd(MapMarkdownStyle(segment.Style, theme));
            col = screen.WriteText(frame.Y, col, segment.Text, style, frame.Right);
        }
    }

    /// <summary>Markdown 语义样式 → TextStyle（Code 取 permission 色；Link 首版无 OSC 8 纯文本降级）。</summary>
    internal static TextStyle MapMarkdownStyle(MarkdownStyle style, Theme theme) => new()
    {
        Bold = style.HasFlag(MarkdownStyle.Bold),
        Italic = style.HasFlag(MarkdownStyle.Italic),
        Underline = style.HasFlag(MarkdownStyle.Underline),
        Dim = style.HasFlag(MarkdownStyle.Dim),
        Strikethrough = style.HasFlag(MarkdownStyle.Strikethrough),
        Foreground = style.HasFlag(MarkdownStyle.Code) ? theme.Permission : default,
    };

    /// <summary>
    /// 代码行高亮覆写（M7-5）— 段文本已写完后按 token 色段覆写前景（保留 rune 与宽度标记，
    /// 只换 StyleId；色段坐标系 = 行段文本，调用方补 startCol 偏移；同色相邻段由 StylePool intern 合并）。
    /// </summary>
    public static void ApplyCodeHighlight(Screen screen, int row, int startCol, IReadOnlyList<HighlightSpan> spans)
    {
        foreach (var span in spans)
        {
            var styleId = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(span.R, span.G, span.B) });
            for (var offset = 0; offset < span.Length; offset++)
            {
                var col = startCol + span.Start + offset;
                var cell = screen[row, col];
                screen[row, col] = cell with { StyleId = styleId };
            }
        }
    }
}
