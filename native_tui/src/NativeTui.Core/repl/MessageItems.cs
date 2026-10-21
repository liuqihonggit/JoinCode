namespace NativeTui.Core.Repl;

/// <summary>
/// 消息滚动项 — MessageRenderer 四种消息的 IScrollItem 适配器。
/// 每项 = marginTop 1（基准 addMargin）+ 正文 1 行；裁剪由 Screen 越界忽略实现。
/// </summary>

/// <summary>用户消息项（整行灰底 + ❯ subtle + 正文）。</summary>
public sealed class UserMessageItem(string text) : IScrollItem, ICursorNavigable
{
    /// <summary>用户正文（测试断言面 — 排队消息等场景）。</summary>
    public string Text => text;

    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderUser(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, text);
}

/// <summary>助手文本消息项（● text 色 + 正文）。</summary>
public sealed class AssistantMessageItem(string text) : IScrollItem, ICursorNavigable
{
    /// <summary>正文（宿主反馈消息断言面 — M7-6 模型切换反馈等）。</summary>
    public string Text => text;

    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderAssistant(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, text);
}

/// <summary>工具调用消息项（● + bold 工具名 + (参数摘要)）。</summary>
public sealed class ToolCallItem(string toolName, string args) : IScrollItem, ICursorNavigable
{
    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderToolCall(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, toolName, args);
}

/// <summary>工具结果消息项（"  ⎿  " dim 前缀 + 正文）。</summary>
public sealed class ToolResultItem(string text) : IScrollItem, ICursorNavigable
{
    /// <summary>结果正文（测试断言面 — 排队反馈等复用场景）。</summary>
    public string Text => text;

    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderToolResult(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, text);
}

/// <summary>
/// 带补丁的工具结果项（M7-2 StructuredDiff）— summary 行沿用 MessageResponse 形态，
/// 后接 hunk 块（4 空格缩进：header dim / added DiffAddedWord / removed DiffRemovedWord / context dim）；
/// patch 全量渲染（基准 update 渲染完整 diff 不截断 — FileWriteTool/UI.tsx "update renders the full diff"），
/// 仅超 10 行预览上限折叠为 "… +N lines (Ctrl+O)" dim（上限对齐基准 Write MAX_LINES_TO_RENDER=10 防刷屏先例，
/// Ctrl+O 展开交互为 M7 后续项）。
/// </summary>
public sealed class DiffResultItem(string summary, DiffHunk[] patch) : IScrollItem, ICursorNavigable
{
    /// <summary>patch 预览行数上限（summary 之外；超限折叠 — 对齐基准 Write MAX_LINES_TO_RENDER=10 先例）。</summary>
    private const int PreviewPatchRows = 10;

    /// <summary>diff 块缩进列数（对齐青蛙TUI "    " 缩进先例）。</summary>
    private const int IndentCols = 4;

    /// <summary>工具结果摘要（首行 ⎿ 正文）。</summary>
    public string Summary { get; } = summary;

    /// <summary>结构化补丁 hunks（驱动侧断言与渲染共享）。</summary>
    public DiffHunk[] Patch { get; } = patch;

    /// <summary>补丁总行数（每 hunk = header 1 行 + diff 行数）。</summary>
    private int TotalPatchRows => Patch.Sum(static hunk => 1 + hunk.Lines.Length);

    /// <summary>内容高度 = margin 1 + summary 1 + 预览行数（超预览时为预览上限 + 折叠提示 1 行）。</summary>
    public int MeasureHeight(int width) =>
        2 + Math.Min(TotalPatchRows, PreviewPatchRows) + (TotalPatchRows > PreviewPatchRows ? 1 : 0);

    /// <summary>渲染：margin 留空 → summary（⎿ 形态）→ hunk 预览行 → 折叠提示（如有）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        MessageRenderer.RenderToolResult(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, Summary);

        var renderedRows = Math.Min(TotalPatchRows, PreviewPatchRows);
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

        if (TotalPatchRows > PreviewPatchRows)
        {
            MessageRenderer.RenderDiffCollapseHint(screen, RowFrame(frame, row), theme, TotalPatchRows - PreviewPatchRows);
        }
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}

/// <summary>错误消息项（● theme.Error 红色 + 正文，bridge error 事件载体）。</summary>
public sealed class ErrorMessageItem(string text) : IScrollItem, ICursorNavigable
{
    /// <summary>错误正文。</summary>
    public string Text { get; } = text;

    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderError(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, Text);
}
