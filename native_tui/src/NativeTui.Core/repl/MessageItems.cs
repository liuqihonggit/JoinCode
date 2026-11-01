namespace NativeTui.Core.Repl;

/// <summary>
/// 消息滚动项 — MessageRenderer 四种消息的 IScrollItem 适配器。
/// 每项 = marginTop 1（基准 addMargin）+ 正文 1 行；裁剪由 Screen 越界忽略实现。
/// </summary>

/// <summary>transcript `/` 搜索文本匹配（M8-3）— 消息项暴露可搜索文本（大小写不敏感）。</summary>
public interface ITextMatchable
{
    /// <summary>是否包含查询文本。</summary>
    bool ContainsText(string query);
}

/// <summary>用户消息项（整行灰底 + ❯ subtle + 正文）。</summary>
public sealed class UserMessageItem(string text) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>用户正文（测试断言面 — 排队消息等场景）。</summary>
    public string Text => text;

    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderUser(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, text);

    /// <summary>正文匹配。</summary>
    public bool ContainsText(string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
}

/// <summary>助手文本消息项（● text 色 + 正文）。TASK062 结构化：思考时长 &gt; 0 时正文上方渲染
/// dim 思考摘要行（∴ 思考 · ctrl+o 展开推理 — 时长归 spinner 行 "thought for Ns"）；ThinkingContent 非空支持 Ctrl+O 展开 CoT 全文
///（展开态 ▾ + dim 缩进逐行）；MessageId 会话序号（定位/对账，UI 不显示）。</summary>
public sealed class AssistantMessageItem(string text, int thinkingDurationMs = 0, string? thinkingContent = null, int messageId = 0) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>思考内容行数上限守卫（超限折叠为截断提示 — CoT 刷屏防护）。</summary>
    private const int MaxThinkingLines = 200;

    /// <summary>正文（宿主反馈消息断言面 — M7-6 模型切换反馈等）。</summary>
    public string Text => text;

    /// <summary>思考总时长 ms（0 = 无思考不渲染思考行）。</summary>
    public int ThinkingDurationMs => thinkingDurationMs;

    /// <summary>CoT 全文（null = 无内容不可展开）。</summary>
    public string? ThinkingContent => thinkingContent;

    /// <summary>消息序号（会话级 — Ctrl+O 定位/transcript 对账）。</summary>
    public int MessageId => messageId;

    /// <summary>思考块展开态（Ctrl+O 切换 — Toggle）。</summary>
    public bool IsThinkingExpanded { get; private set; }

    /// <summary>切换思考块展开态（有 CoT 才有效；高度变化由下次 MeasureHeight 反映）。</summary>
    public void ToggleThinking() {
        if (thinkingContent is { Length: > 0 }) {
            IsThinkingExpanded = !IsThinkingExpanded;
        }
    }

    /// <summary>内容高度 = margin 1 + 正文 1 + 思考摘要行（有思考）+ CoT 行数（展开态，含截断守卫）。</summary>
    public int MeasureHeight(int width) =>
        2
        + (thinkingDurationMs > 0 ? 1 : 0)
        + (IsThinkingExpanded ? CountThinkingLines() : 0);

    /// <summary>margin 行留空 → 思考摘要行（有思考）→ CoT 全文（展开态）→ 正文。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) {
        var row = frame.Y + 1;
        if (thinkingDurationMs > 0) {
            MessageRenderer.RenderThinkingSummary(screen, frame with { Y = row++, Height = 1 }, theme, thinkingDurationMs, IsThinkingExpanded);
        }

        if (IsThinkingExpanded && thinkingContent is { Length: > 0 }) {
            var lines = CountThinkingLines();
            MessageRenderer.RenderThinkingContent(screen, frame with { Y = row, Height = lines }, theme, TruncateThinking());
            row += lines;
        }

        MessageRenderer.RenderAssistant(screen, frame with { Y = row, Height = 1 }, theme, text);
    }

    /// <summary>CoT 展开行数（含超限截断行）。</summary>
    private int CountThinkingLines() => TruncateThinking().Split('\n').Length;

    /// <summary>CoT 截断守卫 — 超行数上限时截断并附提示行。</summary>
    private string TruncateThinking() {
        if (thinkingContent is not { Length: > 0 })
            return string.Empty;

        var lines = thinkingContent.Split('\n');
        return lines.Length > MaxThinkingLines
            ? string.Join("\n", lines[..MaxThinkingLines]) + $"\n… 思考内容超 {MaxThinkingLines} 行已截断"
            : thinkingContent;
    }

    /// <summary>正文 + CoT 匹配（transcript 搜索）。</summary>
    public bool ContainsText(string query) =>
        text.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (thinkingContent?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>
/// 工具调用消息项（● + bold 工具名 + (参数摘要)）。标题行摘要构造期派生缓存
/// （ToolTitleFormatter — 每帧渲染零 JSON 解析）：Bash 命令 2 行/160 字符截断+多行显示语义
///（截断尾 …，续行缩进对齐参数列）；Read/Write/Edit 路径；Grep/Glob pattern+path 引号包裹；
/// Edit 显示名 Create/Update 派生（old_string 空 = Create）。Args 纯文本降级原文（兼容模拟引擎）。
/// IToolCallInfo — 消息操作域复制面契约（ToolName/Args 公开 — CopyTextExtractor 主参数提取接线）。
/// ITextMatchable — transcript 搜索匹配（toolName/args，M8-3）。
/// </summary>
public sealed class ToolCallItem : IScrollItem, ICursorNavigable, IToolCallInfo, ITextMatchable
{
    /// <summary>显示工具名（Edit Create/Update 派生后的标题行名称）。</summary>
    private readonly string _displayToolName;

    /// <summary>派生后的参数摘要（多行命令含 \n — 高度与渲染按行拆分）。</summary>
    private readonly string _displayArgs;

    /// <summary>构造期拆分的标题行缓存（渲染/测量共享 — 50ms 帧拍零重复 Split）。</summary>
    private readonly string[] _displayLines;

    /// <summary>构造：派生显示名（Edit → Create/Update）+ 参数摘要 + 行拆分（一次性缓存）。</summary>
    public ToolCallItem(string toolName, string args)
    {
        ToolName = toolName;
        Args = args;
        _displayToolName = ToolTitleFormatter.DisplayToolName(toolName, args);
        _displayArgs = ToolTitleFormatter.Format(toolName, args);
        _displayLines = _displayArgs.Split('\n');
    }

    /// <summary>工具名（引擎侧原名 — 测试断言面）。</summary>
    public string ToolName { get; }

    /// <summary>原始参数串（引擎侧原样 — 测试断言面）。</summary>
    public string Args { get; }

    /// <summary>显示工具名（Edit Create/Update 派生后的标题行名称）。</summary>
    public string DisplayToolName => _displayToolName;

    /// <summary>派生后的参数摘要（多行命令含 \n — 高度与渲染按行拆分）。</summary>
    public string DisplayArgs => _displayArgs;

    /// <summary>内容高度 = margin 1 + 标题行数（单行参数 1 + 多行命令续行数）。</summary>
    public int MeasureHeight(int width) => 1 + _displayLines.Length;

    /// <summary>渲染：margin 留空 → 标题行（多行命令按行拆分渲染，续行缩进对齐参数列）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderToolCall(
            screen,
            frame with { Y = frame.Y + 1, Height = _displayLines.Length },
            theme,
            _displayToolName,
            _displayArgs);

    /// <summary>工具名 + 参数匹配。</summary>
    public bool ContainsText(string query) =>
        ToolName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Args.Contains(query, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 工具结果消息项 — 统一四段渲染契约（S1 数据源：ToolResult 事件 RawOutput + HiddenLines）：
/// margin 1 → summary 行（"  ⎿  " dim 前缀 + 默认正文，空 summary 不占行）
/// → patch 块（非空时 — 沿用 StructuredDiff 形态：4 空格缩进 hunk 预览，超 10 行折叠提示）
/// → RawOutput 折叠块（Bash 3 行 / Write 10 行按 PreviewLimit；HiddenLines 引擎侧算好 TUI 直接换算：
/// 差 1 行特例直接显示剩余全部不出提示（terminal.ts L44-54）、≥2 行出 "… +N lines (ctrl+o)" dim 提示、
/// trim 后空显示 "(No output)" dim、summary 空时首输出行与 ⎿ 前缀同行）
/// → 展开态（SetExpanded/ToggleExpand — M8-4 交互统一）patch + RawOutput 全量渲染
/// （ctrl+o 消费完整数据，item 恒持有全量不截断存储；MeasureHeight 随展开态变化 —
/// ScrollBox 每帧全量重渲自动生效，无需失效通知）。
/// 单参构造 = 纯摘要旧形态（无 patch/输出 — 排队反馈等场景兼容）。
/// </summary>
public sealed class ToolResultItem : IScrollItem, ICursorNavigable, ITextMatchable
{

    private static int _hitIdCounter;

    /// <summary>命中标识（TASK079 — 鼠标点击原地展开/收起完整输出；实例序号稳定）。</summary>
    public string? HitTestId { get; }

    /// <summary>构造期拆分的输出行缓存（渲染/测量共享 — 50ms 帧拍零重复 Split；null/空输出为空数组）。</summary>
    private readonly string[] _outputLines;

    /// <summary>gutter 行号位数（TASK066 F1 — 全 patch 统一宽；0 = 畸形 hunk 头无行号降级无 gutter 列）。</summary>
    private readonly int _gutterDigits;

    /// <summary>构造期推导的各 hunk 各行单列行号（TASK066 F1 — 渲染帧拍零解析；外层索引与 Patch 对齐）。</summary>
    private readonly int?[][] _lineNumbers;

    /// <summary>构造期预高亮的各 hunk 各行 token 色段（TASK066 F1 — null = 无高亮能力/语言不支持全降级）。</summary>
    private readonly IReadOnlyList<HighlightSpan>?[][]? _syntaxSpans;

    /// <summary>纯摘要形态（旧调用兼容 — 排队反馈/未登录提示等无输出场景）。</summary>
    public ToolResultItem(string text) : this(text, null, null, 0)
    {
    }

    /// <summary>
    /// 统一契约构造（S1 Driver 消费）：summary 结果摘要 / patch 结构化补丁（非空走 diff 渲染）/
    /// rawOutput 原始终端输出（折叠数据源，item 持有全量供展开）/ hiddenLines 引擎侧算好的隐藏行数 /
    /// previewLimit 预览上限（Bash=3 默认、Write=10 由 Driver 传 ToolFoldingPolicy.MaxWritePreviewLines）/
    /// codeHighlighter 代码高亮插件（TASK066 F1 — null = 高亮禁用降级纯 diff 色，对齐
    /// MarkdownMessageItem(document, highlighter) 注入先例）。
    /// 构造期预计算 gutter 行号 + 语法色段（ToolCallItem._displayLines 先例 — 渲染帧拍零推导成本）。
    /// </summary>
    public ToolResultItem(string summary, DiffHunk[]? patch, string? rawOutput, int hiddenLines,
        int previewLimit = ToolFoldingPolicy.MaxToolOutputLines,
        ICodeHighlighter? codeHighlighter = null)
    {

        HitTestId = $"tool-result:{Interlocked.Increment(ref _hitIdCounter)}";
        Summary = summary;
        Patch = patch;
        RawOutput = rawOutput;
        HiddenLines = hiddenLines;
        PreviewLimit = previewLimit;
        _outputLines = SplitRawOutput(rawOutput);
        _gutterDigits = patch is { Length: > 0 } ? DiffGutter.GutterDigits(patch) : 0;
        _lineNumbers = patch is { Length: > 0 } ? patch.Select(DiffGutter.AssignLineNumbers).ToArray() : [];
        _syntaxSpans = HighlightPatchLines(patch, codeHighlighter, summary);
    }

    /// <summary>结果摘要（首行 ⎿ 正文；空串不占行）。</summary>
    public string Summary { get; }

    /// <summary>结果正文（Summary 别名 — 既有测试断言面复用）。</summary>
    public string Text => Summary;

    /// <summary>结构化补丁 hunks（null = 无补丁不渲染 diff 块）。</summary>
    public DiffHunk[]? Patch { get; }

    /// <summary>原始终端输出全量（null = 无输出块；展开态消费完整数据 — ctrl+o）。</summary>
    public string? RawOutput { get; }

    /// <summary>引擎侧算好的隐藏行数（0 = 无折叠全量显示）。</summary>
    public int HiddenLines { get; }

    /// <summary>输出预览行数上限（Bash 3 / Write 10）。</summary>
    public int PreviewLimit { get; }

    /// <summary>展开态（Ctrl+O 切换 — Driver 接线；展开时 RawOutput 全量渲染）。</summary>
    public bool IsExpanded { get; private set; }

    /// <summary>设置展开态（Driver/S6 接线 — ctrl+o 消费完整 RawOutput；无折叠数据时展开为无操作高度不变）。</summary>
    public void SetExpanded(bool expanded) => IsExpanded = expanded;

    /// <summary>切换展开态（M8-4 交互统一入口 — 上游消息光标 Enter 展开消费；高度变化由下次 MeasureHeight 反映）。</summary>
    public void ToggleExpand() => SetExpanded(!IsExpanded);

    /// <summary>补丁总行数（每 hunk = header 1 行 + diff 行数；无补丁 0）。</summary>
    private int TotalPatchRows => Patch?.Sum(static hunk => 1 + hunk.Lines.Length) ?? 0;

    /// <summary>补丁渲染行数（折叠态超 10 行上限折叠 — 对齐基准 Write MAX_LINES_TO_RENDER=10 先例；展开态全量）。</summary>
    private int RenderedPatchRows => IsExpanded ? TotalPatchRows : Math.Min(TotalPatchRows, ToolFoldingPolicy.MaxWritePreviewLines);

    /// <summary>补丁折叠提示行（折叠态预览外还有行才出 — 展开态提示行消失）。</summary>
    private bool ShowsPatchHint => !IsExpanded && TotalPatchRows > ToolFoldingPolicy.MaxWritePreviewLines;

    /// <summary>输出可见行数 — 无输出 0；空白输出 1（"(No output)" 占位）；展开态全量；折叠态按 HiddenLines 换算。</summary>
    private int VisibleOutputRowCount => RawOutput is null
        ? 0
        : _outputLines.Length == 0
            ? 1
            : IsExpanded
                ? _outputLines.Length
                : ToolFoldingPolicy.VisibleOutputLines(_outputLines.Length, HiddenLines, PreviewLimit);

    /// <summary>输出折叠提示行（差 1 行特例不出提示 — terminal.ts L44-54）。</summary>
    private bool ShowsOutputHint =>
        !IsExpanded && RawOutput is not null && _outputLines.Length > 0 && ToolFoldingPolicy.ShowsCollapseHint(HiddenLines);

    /// <summary>内容高度 = margin 1 + summary 0/1 + patch 预览 + patch 提示 0/1 + 输出行 + 输出提示 0/1。</summary>
    public int MeasureHeight(int width) =>
        1
        + (Summary.Length > 0 ? 1 : 0)
        + RenderedPatchRows + (ShowsPatchHint ? 1 : 0)
        + VisibleOutputRowCount + (ShowsOutputHint ? 1 : 0);

    /// <summary>渲染：margin 留空 → summary ⎿ 行 → patch 预览块 + 折叠提示 → 输出折叠块 + 折叠提示。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var row = RenderSummaryRow(screen, frame, theme, frame.Y + 1);
        row = RenderPatchRows(screen, frame, theme, row);
        RenderOutputRows(screen, frame, theme, row);
    }

    /// <summary>summary 行渲染（空摘要跳过不占行）。</summary>
    private int RenderSummaryRow(Screen screen, Rect frame, Theme theme, int row)
    {
        if (Summary.Length == 0)
        {
            return row;
        }

        MessageRenderer.RenderToolResult(screen, RowFrame(frame, row), theme, Summary);
        return row + 1;
    }

    /// <summary>patch 块渲染 — hunk 预览（header dim / 行按 Kind 着色 + gutter 行号 + 语法色段）+ 超限折叠提示。</summary>
    private int RenderPatchRows(Screen screen, Rect frame, Theme theme, int row)
    {
        var renderedRows = RenderedPatchRows;
        for (var hunkIndex = 0; hunkIndex < (Patch?.Length ?? 0); hunkIndex++)
        {
            var hunk = Patch![hunkIndex];
            if (renderedRows == 0)
            {
                break;
            }

            MessageRenderer.RenderDiffHeader(screen, RowFrame(frame, row), theme, hunk.Header);
            row++;
            renderedRows--;
            for (var lineIndex = 0; lineIndex < hunk.Lines.Length; lineIndex++)
            {
                if (renderedRows == 0)
                {
                    break;
                }

                MessageRenderer.RenderDiffLine(
                    screen,
                    RowFrame(frame, row),
                    theme,
                    hunk.Lines[lineIndex],
                    _lineNumbers[hunkIndex][lineIndex],
                    _gutterDigits,
                    _syntaxSpans?[hunkIndex]?[lineIndex]);
                row++;
                renderedRows--;
            }
        }

        if (ShowsPatchHint)
        {
            MessageRenderer.RenderDiffCollapseHint(screen, RowFrame(frame, row), theme, TotalPatchRows - RenderedPatchRows);
            row++;
        }

        return row;
    }

    /// <summary>构造期预高亮（渲染帧拍零高亮成本 — ToolCallItem._displayLines 构造期缓存先例）：
    /// 语言检测（summary 路径扩展名 → DiffGutter.DetectLanguage）+ 逐 hunk new 侧行会话高亮；
    /// 无高亮器 / 无语言 / 空 patch → null 全降级纯 diff 色（高亮失败不阻塞渲染）。</summary>
    private static IReadOnlyList<HighlightSpan>?[][]? HighlightPatchLines(DiffHunk[]? patch, ICodeHighlighter? highlighter, string summary)
    {
        if (patch is not { Length: > 0 } || highlighter is null || DiffGutter.DetectLanguage(summary) is not { } language)
        {
            return null;
        }

        return patch.Select(hunk => DiffGutter.HighlightHunkLines(hunk, highlighter, language)).ToArray();
    }

    /// <summary>输出块渲染 — 空白输出 "(No output)" dim；折叠首行（空 summary 时与 ⎿ 同行）/ 续行 5 列缩进 + 折叠提示。</summary>
    private int RenderOutputRows(Screen screen, Rect frame, Theme theme, int row)
    {
        if (RawOutput is null)
        {
            return row;
        }

        if (_outputLines.Length == 0)
        {
            MessageRenderer.RenderNoOutput(screen, RowFrame(frame, row), theme);
            return row + 1;
        }

        var visible = IsExpanded
            ? _outputLines.Length
            : ToolFoldingPolicy.VisibleOutputLines(_outputLines.Length, HiddenLines, PreviewLimit);
        for (var i = 0; i < visible; i++)
        {
            if (i == 0 && Summary.Length == 0)
            {
                MessageRenderer.RenderToolResult(screen, RowFrame(frame, row), theme, _outputLines[0].TrimEnd('\r'));
            }
            else
            {
                MessageRenderer.RenderToolOutputLine(screen, RowFrame(frame, row), theme, _outputLines[i]);
            }

            row++;
        }

        if (ShowsOutputHint)
        {
            MessageRenderer.RenderToolOutputHint(screen, RowFrame(frame, row), theme, HiddenLines);
            row++;
        }

        return row;
    }

    /// <summary>拆分输出行 — null 空数组；尾部空白修剪（基准 renderTruncatedContent trimEnd）后空为无输出占位；\r 行尾清理。</summary>
    private static string[] SplitRawOutput(string? rawOutput)
    {
        if (rawOutput is null)
        {
            return [];
        }

        var trimmed = rawOutput.TrimEnd();
        return trimmed.Length == 0 ? [] : trimmed.Split('\n');
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };

    /// <summary>摘要 + diff 行内容匹配（transcript 搜索）。</summary>
    public bool ContainsText(string query) =>
        Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (Patch?.Any(hunk => hunk.Lines.Any(line => line.Content.Contains(query, StringComparison.OrdinalIgnoreCase))) ?? false);
}

/// <summary>
/// 带补丁的工具结果项（M7-2 StructuredDiff）— 薄委托 ToolResultItem（summary + patch 契约子集，
/// 渲染/测量逻辑唯一来源 = core；既有 Driver 构造签名兼容）。patch 全量渲染
/// （基准 update 渲染完整 diff 不截断 — FileWriteTool/UI.tsx "update renders the full diff"），
/// 仅超 10 行预览上限折叠为 "… +N lines (Ctrl+O)" dim（上限对齐基准 Write MAX_LINES_TO_RENDER=10 防刷屏先例）。
/// codeHighlighter（TASK066 F1 — 可选尾参默认 null 零破坏）：透传 core 驱动 gutter 行号 +
/// 行级语法高亮增强；null = 纯 diff 色降级现形态。
/// </summary>
public sealed class DiffResultItem(string summary, DiffHunk[] patch, ICodeHighlighter? codeHighlighter = null) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>渲染/测量核心（统一契约构造 — 无 RawOutput 的纯 patch 形态 + 高亮器透传）。</summary>
    private readonly ToolResultItem _core = new(summary, patch, null, 0, codeHighlighter: codeHighlighter);

    /// <summary>工具结果摘要（首行 ⎿ 正文）。</summary>
    public string Summary => summary;

    /// <summary>结构化补丁 hunks（驱动侧断言与渲染共享）。</summary>
    public DiffHunk[] Patch => patch;

    /// <summary>内容高度委托 core（margin 1 + summary 1 + 预览行数 + 折叠提示）。</summary>
    public int MeasureHeight(int width) => _core.MeasureHeight(width);

    /// <summary>渲染委托 core（summary ⎿ 行 → hunk 预览 → 折叠提示）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) => _core.Render(screen, frame, theme);

    /// <summary>展开态委托 core（M8-4 展开交互 — 上游消息光标 Enter 消费面）。</summary>
    public bool IsExpanded => _core.IsExpanded;

    /// <summary>切换展开态委托 core（高度变化由下次 MeasureHeight 反映）。</summary>
    public void ToggleExpand() => _core.ToggleExpand();

    /// <summary>摘要 + diff 行内容匹配（委托 core — transcript 搜索）。</summary>
    public bool ContainsText(string query) => _core.ContainsText(query);
}

/// <summary>错误消息项（● theme.Error 红色 + 正文，bridge error 事件载体）。</summary>
public sealed class ErrorMessageItem(string text) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>错误正文。</summary>
    public string Text { get; } = text;

    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderError(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, Text);

    /// <summary>错误正文匹配。</summary>
    public bool ContainsText(string query) => Text.Contains(query, StringComparison.OrdinalIgnoreCase);
}

/// <summary>启动诊断行项 — 纯文本 dim 渲染（引擎启动中/[diag] 等）。
/// 纳入 TUI 渲染内容使应用内选区覆盖（无需 Shift 选择）。</summary>
public sealed class StartupLineItem(string text) : IScrollItem
{
    /// <summary>诊断行文本。</summary>
    public string Text { get; } = text;

    /// <summary>单行高度。</summary>
    public int MeasureHeight(int width) => 1;

    /// <summary>dim 样式渲染纯文本行。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, Text, dim, frame.Right);
    }
}
