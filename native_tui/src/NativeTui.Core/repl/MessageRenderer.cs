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
        // TASK069 B-3 — 换行符渲染为空格分隔（'\n' 宽度 0 被 WriteText 跳过会吞并成 "ab"）；
        // CRLF 折叠单空格。1 行高度语义保持（多行完整支持属 TASK069 差异项 M1 待决策）
        var flattened = text.Replace("\r\n", "\n").Replace('\n', ' ').Replace('\r', ' ');
        screen.WriteText(frame.Y, frame.X + 2, flattened, body, textRight);
    }

    /// <summary>渲染助手文本消息：● + 空格（minWidth 2）+ 正文，全部 text 色。</summary>
    public static void RenderAssistant(Screen screen, Rect frame, Theme theme, string text)
    {
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        screen.WriteText(frame.Y, frame.X, $"{Figures.MessageBullet} ", style, frame.Right);
        screen.WriteText(frame.Y, frame.X + 2, text, style, frame.Right);
    }

    /// <summary>
    /// 渲染工具调用消息：● + 空格 + bold 工具名 + 默认样式 (参数摘要)，名字与摘要间无间隔。
    /// 参数含换行时多行显示（Bash 多行命令 — 基准 Ink wrap 语义）：首行 "(" + 首段不闭合，
    /// 续行缩进对齐参数列（工具名后括号列），末行尾闭合括号（基准 AssistantToolUseMessage 括号包全文）。
    /// </summary>
    public static void RenderToolCall(Screen screen, Rect frame, Theme theme, string toolName, string args)
    {
        var bullet = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(frame.Y, frame.X, $"{Figures.MessageBullet} ", bullet, frame.Right);
        var col = screen.WriteText(frame.Y, frame.X + 2, toolName, bold, frame.Right);
        var lines = args.Split('\n');
        var firstSegment = lines[0].TrimEnd('\r');
        screen.WriteText(frame.Y, col, lines.Length == 1 ? $"({firstSegment})" : $"({firstSegment}", 0, frame.Right);
        for (var i = 1; i < lines.Length; i++)
        {
            var text = lines[i].TrimEnd('\r');
            var isLast = i == lines.Length - 1;
            screen.WriteText(frame.Y + i, col, isLast ? $"{text})" : text, 0, frame.Right);
        }
    }

    /// <summary>渲染工具结果消息："  ⎿  " 前缀整体 dim（单一 Text span），正文默认样式。</summary>
    public static void RenderToolResult(Screen screen, Rect frame, Theme theme, string text)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, Figures.ToolResultIndent, dim, frame.Right);
        screen.WriteText(frame.Y, frame.X + Figures.ToolResultIndent.Length, text, 0, frame.Right);
    }

    /// <summary>渲染思考摘要行（结构化消息）：∴ 前缀 + "思考"（收起态带 " · ctrl+o 展开推理" /
    /// 展开态 " ▾"），整行 dim。时长不在此行显示 — 归 spinner 行 "thought for Ns"（对齐基准
    /// SpinnerAnimationRow thinkingStatus 口径；durationMs 参数仅为既有调用方兼容保留，
    /// 渲染不消费）。</summary>
    public static void RenderThinkingSummary(Screen screen, Rect frame, Theme theme, int durationMs, bool expanded)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var suffix = expanded ? " ▾" : " · ctrl+o 展开推理";
        screen.WriteText(frame.Y, frame.X, $"{Figures.ThereforeSign} 思考{suffix}", dim, frame.Right);
    }

    /// <summary>渲染思考内容行（展开态 CoT 全文）：⎿ + 2 空格缩进，dim 基调 Markdown 行内渲染
    ///（TASK064/TASK065 P3）— 行内语法经 InlineMarkdownParser 轻量解析（**bold**/*italic*/`code` 三件套），
    /// 段样式叠 Dim 基调（基准思考块 = dim Markdown）；无行内语法行/列表/引用/标题等块级行 = 纯文本 dim
    ///（现行为完整保留）；截断守卫在消息项 TruncateThinking — 行数按 \n 计与段渲染无关。</summary>
    public static void RenderThinkingContent(Screen screen, Rect frame, Theme theme, string content)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var indent = $"{Figures.ToolResultIndent}  ";
        var row = frame.Y;
        foreach (var line in content.Split('\n')) {
            var text = line.TrimEnd('\r');
            if (text.Length > 0) {
                screen.WriteText(row, frame.X, indent, dim, frame.Right);
                RenderThinkingInline(screen, row, frame.X + indent.Length, frame.Right, theme, text, dim);
            }
            row++;
        }
    }

    /// <summary>CoT 行内段渲染 — 有行内语法走段写（样式经 MapMarkdownStyle 映射后叠 Dim 基调：
    /// bold/italic 保持 dim、code permission 前景 + dim）；无语法纯文本 dim 单写（降级现行为）。</summary>
    private static void RenderThinkingInline(Screen screen, int row, int col, int right, Theme theme, string text, int dimStyleId)
    {
        var segments = InlineMarkdownParser.ParseLine(text);
        if (segments.Count == 0)
        {
            screen.WriteText(row, col, text, dimStyleId, right);
            return;
        }

        foreach (var segment in segments)
        {
            var style = screen.Styles.GetOrAdd(MapMarkdownStyle(segment.Style, theme) with { Dim = true });
            col = screen.WriteText(row, col, segment.Text, style, right);
        }
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
    /// 渲染单个 diff 行（TASK066 板块 F 基建 F1 — 行号 gutter + 语法高亮增强）：
    /// 布局 = 4 空格缩进 + 行号 gutter（' ' + 右对齐 digits 位 + ' '，基准 color-diff addLineNumber
    /// L726-745 同形态）+ marker（+/-/空格）+ 正文；行号 null 但 digits&gt;0 时空占位（wrap 续行对齐语义）；
    /// digits=0（畸形 hunk 头全 patch 无行号）降级旧无 gutter 形态。
    /// 着色：added/removed 行整行背景 DiffAdded/DiffRemoved 满宽填充（基准 wrapText L713-723 pad 到行尾 —
    /// solid 红绿条）+ gutter/marker 前景 DiffAddedWord/DiffRemovedWord（decoration 语义，基准
    /// decorationColor）；context 行整行 dim 无背景（现形态保留）。
    /// 语法叠加：spans 非 null 时正文按段切写 — 段前景 = token RGB、背景保持行底色（基准 applyBackground
    /// L768-817 只换背景保前景的 w2 镜像：前景域语法色 + 背景域 diff 色正交叠加）；Removed 行恒无 spans
    ///（调用侧不提供 — 基准 marker==='-' 不高亮）；spans 坐标 = Content 的 UTF-16 字符区间，分段写由
    /// WriteText 返回列推进对齐宽字符。
    /// </summary>
    /// <param name="line">diff 行（Kind 决定 marker 与配色）。</param>
    /// <param name="lineNumber">单列行号（null = 该行无行号 — gutter 空占位）。</param>
    /// <param name="gutterDigits">gutter 行号位数（0 = 整块降级无 gutter 列）。</param>
    /// <param name="syntaxSpans">该行 token 色段（null/空 = 无语法高亮纯 diff 色正文）。</param>
    public static void RenderDiffLine(Screen screen, Rect frame, Theme theme, DiffLine line, int? lineNumber, int gutterDigits, IReadOnlyList<HighlightSpan>? syntaxSpans)
    {
        var marker = line.Kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "-",
            _ => " ",
        };
        var gutter = GutterText(lineNumber, gutterDigits);

        if (line.Kind is DiffLineKind.Added or DiffLineKind.Removed)
        {
            RenderChangedDiffLine(screen, frame, theme, line, marker, gutter, syntaxSpans);
            return;
        }

        // context 行：4 缩进 + gutter + 空格 marker + dim 正文（现形态 + gutter 列；基准 context 无背景 dimColor）
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"    {gutter}{marker}{line.Content}", dim, frame.Right);
    }

    /// <summary>gutter 列文本 — 有行号 ' ' + 右对齐 digits 位 + ' '；无行号但 digits&gt;0 空占位
    ///（基准 wrap 续行 ' '.repeat(maxDigits+2) 对齐语义）；digits=0 空串（降级无 gutter）。</summary>
    private static string GutterText(int? lineNumber, int gutterDigits) =>
        gutterDigits <= 0
            ? string.Empty
            : lineNumber is { } number
                ? $" {number.ToString().PadLeft(gutterDigits)} "
                : new string(' ', gutterDigits + 2);

    /// <summary>added/removed 行渲染 — 满行背景 + gutter/marker decoration 前景 + 正文语法段切写。</summary>
    private static void RenderChangedDiffLine(Screen screen, Rect frame, Theme theme, DiffLine line, string marker, string gutter, IReadOnlyList<HighlightSpan>? syntaxSpans)
    {
        var background = line.Kind == DiffLineKind.Added ? theme.DiffAdded : theme.DiffRemoved;
        var decoration = line.Kind == DiffLineKind.Added ? theme.DiffAddedWord : theme.DiffRemovedWord;

        // 满行背景填充（基准 solid 红绿条 — 语法前景与 diff 背景正交叠加的底板）
        var fill = screen.Styles.GetOrAdd(new TextStyle { Background = background });
        screen.WriteText(frame.Y, frame.X, new string(' ', frame.Width), fill, frame.Right);

        // gutter + marker（decoration 前景 + 行背景 — 基准 addLineNumber/addMarker 样式合成）
        var gutterStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = decoration, Background = background });
        var col = screen.WriteText(frame.Y, frame.X, $"    {gutter}{marker}", gutterStyle, frame.Right);

        // 正文：无语法段 = 默认前景 + 行背景（基准 defaultStyle fg=theme.foreground）；
        // 有语法段 = 段内 token RGB 前景 + 行背景，段间隙默认前景（背景恒保持 — applyBackground 语义）
        var bodyStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text, Background = background });
        if (syntaxSpans is null || syntaxSpans.Count == 0)
        {
            screen.WriteText(frame.Y, col, line.Content, bodyStyle, frame.Right);
            return;
        }

        var content = line.Content.AsSpan();
        var prevEnd = 0;
        foreach (var span in syntaxSpans)
        {
            var start = Math.Clamp(span.Start, 0, content.Length);
            var end = Math.Clamp(span.Start + span.Length, start, content.Length);
            if (end <= start)
            {
                continue;
            }

            if (start > prevEnd)
            {
                col = screen.WriteText(frame.Y, col, content[prevEnd..start], bodyStyle, frame.Right);
            }

            var syntaxStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(span.R, span.G, span.B), Background = background });
            col = screen.WriteText(frame.Y, col, content[start..end], syntaxStyle, frame.Right);
            prevEnd = end;
        }

        if (prevEnd < content.Length)
        {
            screen.WriteText(frame.Y, col, content[prevEnd..], bodyStyle, frame.Right);
        }
    }

    /// <summary>渲染折叠提示："    … +N lines (Ctrl+O)"（dim；Ctrl+O 切换 DiffResultItem.IsExpanded — M8-4 展开后此行消失）。</summary>
    public static void RenderDiffCollapseHint(Screen screen, Rect frame, Theme theme, int hiddenLines)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"    … +{hiddenLines} lines (Ctrl+O)", dim, frame.Right);
    }

    /// <summary>
    /// 渲染工具输出续行（折叠首行之后的后续行）— ToolResultIndent 等宽空格缩进（5 列）+ 行内容默认样式
    ///（基准 MessageResponse row 布局：⎿ 前缀列固定，续行在右列 wrap 对齐；行尾 \r 去除 — 模型输出统一 \n）。
    /// </summary>
    public static void RenderToolOutputLine(Screen screen, Rect frame, Theme theme, string line)
    {
        var indent = new string(' ', Figures.ToolResultIndent.Length);
        screen.WriteText(frame.Y, frame.X, $"{indent}{line.TrimEnd('\r')}", 0, frame.Right);
    }

    /// <summary>
    /// 渲染工具输出折叠提示行 — ToolResultIndent 等宽缩进 + "… +N lines (ctrl+o)" 整行 dim
    ///（基准 terminal.ts renderTruncatedContent L107 ctrlOToExpand 展开引导）。
    /// </summary>
    public static void RenderToolOutputHint(Screen screen, Rect frame, Theme theme, int hiddenLines)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var indent = new string(' ', Figures.ToolResultIndent.Length);
        screen.WriteText(frame.Y, frame.X, $"{indent}{ToolFoldingPolicy.HiddenLinesHint(hiddenLines)}", dim, frame.Right);
    }

    /// <summary>渲染无输出占位行 — "  ⎿  (No output)" 整行 dim（BashToolResultMessage.tsx L156 stdout/stderr 皆空）。</summary>
    public static void RenderNoOutput(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"{Figures.ToolResultIndent}{ToolFoldingPolicy.NoOutputText}", dim, frame.Right);
    }

    /// <summary>
    /// 渲染工具分组聚合摘要行（CollapsedReadSearchContent.tsx L448-461 汉化形态）：
    /// ● + 摘要文案 + 活动态 "…" + dim " (ctrl+o)"（活动态 ● 600ms 方波闪烁 — 基准 ToolUseLoader+useBlink，
    /// 与 RenderSubagentHeader 同相位口径）；完成态整行 dim（基准 dimColor={!isActiveGroup}）。
    /// </summary>
    public static void RenderCollapsedGroupSummary(Screen screen, Rect frame, Theme theme, string text, bool isActive, long animTimeMs)
    {
        const int BlinkIntervalMs = 600;
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var bullet = !isActive || animTimeMs / BlinkIntervalMs % 2 == 0 ? Figures.MessageBullet : " ";
        screen.WriteText(frame.Y, frame.X, $"{bullet} ", dim, frame.Right);
        if (isActive)
        {
            var col = screen.WriteText(frame.Y, frame.X + 2, $"{text}…", 0, frame.Right);
            screen.WriteText(frame.Y, col, " (ctrl+o)", dim, frame.Right);
        }
        else
        {
            screen.WriteText(frame.Y, frame.X + 2, text, dim, frame.Right);
        }
    }

    /// <summary>
    /// 渲染小终端塌缩进度单行（AgentTool/UI.tsx L495-502 condensed 形态）：
    /// "  ⎿  In progress… · N tool use(s)" — 前缀与文案 dim、计数 bold（单数 tool use / 复数 tool uses）。
    /// </summary>
    public static void RenderCondensedToolProgress(Screen screen, Rect frame, Theme theme, int toolUses)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(frame.Y, frame.X, Figures.ToolResultIndent, dim, frame.Right);
        var col = screen.WriteText(frame.Y, frame.X + Figures.ToolResultIndent.Length, "In progress… · ", dim, frame.Right);
        col = screen.WriteText(frame.Y, col, toolUses.ToString(), bold, frame.Right);
        screen.WriteText(frame.Y, col, toolUses == 1 ? " tool use" : " tool uses", dim, frame.Right);    }

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

    /// <summary>渲染模型选择器标题行 — 1 空格缩进 + bold permission 色（M7-6，基准 "Select model" remember bold）。</summary>
    public static void RenderModelTitle(Screen screen, Rect frame, Theme theme, string title)
    {
        var style = screen.Styles.GetOrAdd(new TextStyle { Bold = true, Foreground = theme.Permission });
        screen.WriteText(frame.Y, frame.X, $" {title}", style, frame.Right);
    }

    /// <summary>渲染模型选择器副标题行 — 1 空格缩进整行 dim（基准 "Switch between Claude models..."）。</summary>
    public static void RenderModelSubtitle(Screen screen, Rect frame, Theme theme, string subtitle)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $" {subtitle}", dim, frame.Right);
    }

    /// <summary>
    /// 渲染模型列表行（M7-6，基准 CustomSelect + ListItem）：
    /// 焦点 = ❯ suggestion + "N. " dim + 名称 suggestion + 当前项尾 ✓ success；
    /// 非焦点 = 空格指示 + "N. " dim + 名称默认样式（当前项尾 ✓ success 恒显）。
    /// </summary>
    public static void RenderModelListRow(Screen screen, Rect frame, Theme theme, int index, string displayName, bool isCurrent, bool focused)
    {
        if (focused)
        {
            var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion });
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            screen[frame.Y, frame.X] = new Cell(new Rune('❯'), suggestion, CellWidth.Default);
            screen.WriteText(frame.Y, frame.X + 1, $" {index + 1}. ", dim, frame.Right);
            var col = screen.WriteText(frame.Y, frame.X + 5, displayName, suggestion, frame.Right);
            if (isCurrent)
            {
                var success = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Success });
                screen.WriteText(frame.Y, col, " ✓", success, frame.Right);
            }

            return;
        }

        var plain = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"  {index + 1}. ", plain, frame.Right);
        var nameCol = screen.WriteText(frame.Y, frame.X + 5, displayName, 0, frame.Right);
        if (isCurrent)
        {
            var success = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Success });
            screen.WriteText(frame.Y, nameCol, " ✓", success, frame.Right);
        }
    }

    /// <summary>渲染模型描述行 — 5 空格缩进（对齐名称行列起点）整行 dim（基准 description inactive 色）。</summary>
    public static void RenderModelDescription(Screen screen, Rect frame, Theme theme, string description)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $"     {description}", dim, frame.Right);
    }

    /// <summary>渲染模型溢出行 — 1 空格缩进 dim "and N more…"（基准 hiddenCount 折叠提示）。</summary>
    public static void RenderModelOverflow(Screen screen, Rect frame, Theme theme, int hiddenCount)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $" and {hiddenCount} more…", dim, frame.Right);
    }

    /// <summary>渲染模型选择器快捷键行 — 1 空格缩进 dim（基准 Byline "Enter confirm · Esc exit"）。</summary>
    public static void RenderModelShortcuts(Screen screen, Rect frame, Theme theme, string shortcuts)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, $" {shortcuts}", dim, frame.Right);
    }

    /// <summary>渲染模型选择器 effort 行（TASK066 D5 — 基准 ModelPicker.tsx:328 支持分支）：
    /// 1 缩进整行 dim（基准外层 Text dimColor），"○/◐/●/◉ Low|Medium|High|Max effort" +
    /// 默认档尾缀 " (default)"（基准 displayEffort === focusedDefaultEffort）+ " ← → to adjust" 调节提示。</summary>
    public static void RenderModelEffortRow(Screen screen, Rect frame, Theme theme, PickerEffortLevel level, bool isDefault)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var defaultSuffix = isDefault ? " (default)" : string.Empty;
        screen.WriteText(frame.Y, frame.X, $" {level.ToSymbol()} {level.ToDisplayName()} effort{defaultSuffix} ← → to adjust", dim, frame.Right);
    }

    /// <summary>渲染模型选择器 effort 不支持行（TASK066 D5 — 基准 ModelPicker.tsx:328 else 分支）：
    /// 1 缩进整行 dim（基准 subtle 色），"○ Effort not supported for {焦点模型名}"
    ///（基准 EffortLevelIndicator effort=undefined → low 符号 ○；模型名缺省省略 for 短语）。</summary>
    public static void RenderModelEffortUnsupportedRow(Screen screen, Rect frame, Theme theme, string? focusedModelName)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var forSuffix = string.IsNullOrEmpty(focusedModelName) ? string.Empty : $" for {focusedModelName}";
        screen.WriteText(frame.Y, frame.X, $" {PickerEffortLevel.Low.ToSymbol()} Effort not supported{forSuffix}", dim, frame.Right);
    }

    /// <summary>
    /// 渲染子代理组头行（M7-7，基准 ToolUseLoader + useBlink）：
    /// ● + 空格 + 文案（"Running N {type} agents…" / "N {type} agents finished"）。
    /// 闪烁：运行中且无错时 600ms 方波 ●↔空格（基准 BLINK_INTERVAL_MS，布局不跳动 — 文案恒 X+2 起）；
    /// 错误恒亮、完成恒亮（基准 t1 = isError || !isUnresolved || isBlinking ? ● : " "）。
    /// 色：运行中 dim 默认前景（基准 isUnresolved → undefined+dimColor，优先于错误红）/ 错误 Error 红 / 完成 Success 绿。
    /// </summary>
    public static void RenderSubagentHeader(Screen screen, Rect frame, Theme theme, string text, bool isDone, bool hasError, long animTimeMs)
    {
        const int BlinkIntervalMs = 600;
        var isRunning = !isDone;
        var bullet = hasError || isDone || animTimeMs / BlinkIntervalMs % 2 == 0 ? Figures.MessageBullet : " ";
        var bulletStyle = isRunning
            ? screen.Styles.GetOrAdd(new TextStyle { Dim = true })
            : screen.Styles.GetOrAdd(new TextStyle { Foreground = hasError ? theme.Error : theme.Success });
        screen.WriteText(frame.Y, frame.X, $"{bullet} ", bulletStyle, frame.Right);
        screen.WriteText(frame.Y, frame.X + 2, text, 0, frame.Right);
    }

    /// <summary>
    /// 渲染子代理状态行（M7-7，基准 AgentProgressLine 首行）：
    /// 树形前缀 "├─ "/"└─ "（组末条目收口）dim + 类型名八色（hideType 时描述提升行首、空描述兜底显示类型）+
    /// " (描述)" + " · N tool uses" + " · M tokens" 统计 dim（零令牌省略 token 段）；
    /// 运行中整行 dim（各段样式叠加 Dim=true）。
    /// </summary>
    public static void RenderSubagentStatusRow(Screen screen, Rect frame, Theme theme, bool isLastOfGroup, SubagentEntry entry, bool isRunning, bool hideType)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var typeStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.SubagentColor(entry.AgentType), Dim = isRunning });
        var bodyStyle = isRunning ? dim : 0;
        screen.WriteText(frame.Y, frame.X, isLastOfGroup ? "└─ " : "├─ ", dim, frame.Right);
        var col = hideType && !string.IsNullOrEmpty(entry.Description)
            ? screen.WriteText(frame.Y, frame.X + 3, entry.Description, bodyStyle, frame.Right)
            : screen.WriteText(frame.Y, frame.X + 3, entry.AgentType, typeStyle, frame.Right);
        if (!hideType && !string.IsNullOrEmpty(entry.Description))
        {
            col = screen.WriteText(frame.Y, col, $" ({entry.Description})", bodyStyle, frame.Right);
        }

        col = screen.WriteText(frame.Y, col, $" · {entry.ToolUseCount} tool uses", dim, frame.Right);
        if (entry.TokenCount > 0)
        {
            screen.WriteText(frame.Y, col, $" · {FormatTokens(entry.TokenCount)} tokens", dim, frame.Right);
        }
    }

    /// <summary>
    /// 渲染子代理活动行（M7-7，基准 AgentProgressLine 次行）：
    /// 树延续前缀 "│  ⎿ "/"   ⎿ "（组末条目空格收口）dim + 活动文本（运行中 dim — 基准整行 dim）。
    /// </summary>
    public static void RenderSubagentActivityRow(Screen screen, Rect frame, Theme theme, bool isLastOfGroup, string text, bool isRunning)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var prefix = isLastOfGroup ? "   ⎿ " : "│  ⎿ ";
        screen.WriteText(frame.Y, frame.X, prefix, dim, frame.Right);
        screen.WriteText(frame.Y, frame.X + prefix.Length, text, isRunning ? dim : 0, frame.Right);
    }

    /// <summary>令牌数格式化 — 0 省略 / &lt;1000 原数 / ≥1000 "N.Nk"（基准 "34.5k tokens" 形态）。</summary>
    internal static string FormatTokens(int tokens) =>
        tokens < 1000 ? tokens.ToString() : $"{tokens / 1000.0:0.#}k";

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

    /// <summary>Markdown 语义样式 → TextStyle（Code 取 permission 色；Link 取 Suggestion 色 +
    /// 下划线 — DSG043 P3 视觉决策：基准 chalk.blue 超链接惯例的 theme 键映射，复用现有键零主题改动）。</summary>
    internal static TextStyle MapMarkdownStyle(MarkdownStyle style, Theme theme) => new()
    {
        Bold = style.HasFlag(MarkdownStyle.Bold),
        Italic = style.HasFlag(MarkdownStyle.Italic),
        Underline = style.HasFlag(MarkdownStyle.Underline) || style.HasFlag(MarkdownStyle.Link),
        Dim = style.HasFlag(MarkdownStyle.Dim),
        Strikethrough = style.HasFlag(MarkdownStyle.Strikethrough),
        Foreground = style.HasFlag(MarkdownStyle.Code)
            ? theme.Permission
            : style.HasFlag(MarkdownStyle.Link) ? theme.Suggestion : default,
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
