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
/// dim 思考摘要行（⎿ 思考 {s} · ctrl+o 展开推理）；ThinkingContent 非空支持 Ctrl+O 展开 CoT 全文
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

/// <summary>工具调用消息项（● + bold 工具名 + (参数摘要)）。</summary>
public sealed class ToolCallItem(string toolName, string args) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderToolCall(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, toolName, args);

    /// <summary>工具名 + 参数匹配。</summary>
    public bool ContainsText(string query) =>
        toolName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || args.Contains(query, StringComparison.OrdinalIgnoreCase);
}

/// <summary>工具结果消息项（"  ⎿  " dim 前缀 + 正文）。</summary>
public sealed class ToolResultItem(string text) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>结果正文（测试断言面 — 排队反馈等复用场景）。</summary>
    public string Text => text;

    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderToolResult(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, text);

    /// <summary>结果正文匹配。</summary>
    public bool ContainsText(string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 带补丁的工具结果项（M7-2 StructuredDiff）— summary 行沿用 MessageResponse 形态，
/// 后接 hunk 块（4 空格缩进：header dim / added DiffAddedWord / removed DiffRemovedWord / context dim）；
/// patch 全量渲染（基准 update 渲染完整 diff 不截断 — FileWriteTool/UI.tsx "update renders the full diff"），
/// 仅超 10 行预览上限折叠为 "… +N lines (Ctrl+O)" dim（上限对齐基准 Write MAX_LINES_TO_RENDER=10 防刷屏先例）；
/// M8-4 工具折叠交互统一：IsExpanded/ToggleExpand 切换展开态 — 展开全量渲染 patch（折叠提示行消失）、
/// 折叠回 10 行预览 + 提示行；MeasureHeight 随展开态变化（ScrollBox 每帧全量重渲自动生效 — 无需失效通知）。
/// </summary>
public sealed class DiffResultItem(string summary, DiffHunk[] patch) : IScrollItem, ICursorNavigable, ITextMatchable
{
    /// <summary>patch 预览行数上限（summary 之外；超限折叠 — 对齐基准 Write MAX_LINES_TO_RENDER=10 先例）。</summary>
    private const int PreviewPatchRows = 10;

    /// <summary>diff 块缩进列数（对齐青蛙TUI "    " 缩进先例）。</summary>
    private const int IndentCols = 4;

    /// <summary>工具结果摘要（首行 ⎿ 正文）。</summary>
    public string Summary { get; } = summary;

    /// <summary>结构化补丁 hunks（驱动侧断言与渲染共享）。</summary>
    public DiffHunk[] Patch { get; } = patch;

    /// <summary>展开态（Ctrl+O 切换 — M8-4 工具折叠交互统一）：true = 全量渲染 patch，false = 10 行预览 + 折叠提示。</summary>
    public bool IsExpanded { get; private set; }

    /// <summary>切换展开态（高度变化由下次 MeasureHeight 反映 — 对齐 ToggleThinking 先例）。</summary>
    public void ToggleExpand() => IsExpanded = !IsExpanded;

    /// <summary>补丁总行数（每 hunk = header 1 行 + diff 行数）。</summary>
    private int TotalPatchRows => Patch.Sum(static hunk => 1 + hunk.Lines.Length);

    /// <summary>内容高度 = margin 1 + summary 1 + 展开全量 patch 行数 / 折叠 min(总行数, 预览上限) + 超限折叠提示 1。</summary>
    public int MeasureHeight(int width) =>
        2 + (IsExpanded
            ? TotalPatchRows
            : Math.Min(TotalPatchRows, PreviewPatchRows) + (TotalPatchRows > PreviewPatchRows ? 1 : 0));

    /// <summary>渲染：margin 留空 → summary（⎿ 形态）→ hunk 行（展开全量 / 折叠预览上限）→ 折叠提示（折叠态超限时）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        MessageRenderer.RenderToolResult(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, Summary);

        var renderedRows = Math.Min(TotalPatchRows, IsExpanded ? TotalPatchRows : PreviewPatchRows);
        var row = frame.Y + 2;
        foreach (var hunk in Patch)
        {
            if (renderedRows == 0)
            {
                break;
            }

            MessageRenderer.RenderDiffHeader(screen, RowFrame(frame, row), theme, hunk.Header);
            row++;
            renderedRows--;
            foreach (var line in hunk.Lines)
            {
                if (renderedRows == 0)
                {
                    break;
                }

                MessageRenderer.RenderDiffLine(screen, RowFrame(frame, row), theme, line);
                row++;
                renderedRows--;
            }
        }

        if (!IsExpanded && TotalPatchRows > PreviewPatchRows)
        {
            MessageRenderer.RenderDiffCollapseHint(screen, RowFrame(frame, row), theme, TotalPatchRows - PreviewPatchRows);
        }
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };

    /// <summary>摘要 + diff 行内容匹配（transcript 搜索）。</summary>
    public bool ContainsText(string query) =>
        Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Patch.Any(hunk => hunk.Lines.Any(line => line.Content.Contains(query, StringComparison.OrdinalIgnoreCase)));
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
