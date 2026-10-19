namespace NativeTui.Core.Repl;

/// <summary>
/// 消息滚动项 — MessageRenderer 四种消息的 IScrollItem 适配器。
/// 每项 = marginTop 1（基准 addMargin）+ 正文 1 行；裁剪由 Screen 越界忽略实现。
/// </summary>

/// <summary>用户消息项（整行灰底 + ❯ subtle + 正文）。</summary>
public sealed class UserMessageItem(string text) : IScrollItem
{
    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderUser(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, text);
}

/// <summary>助手文本消息项（● text 色 + 正文）。</summary>
public sealed class AssistantMessageItem(string text) : IScrollItem
{
    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderAssistant(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, text);
}

/// <summary>工具调用消息项（● + bold 工具名 + (参数摘要)）。</summary>
public sealed class ToolCallItem(string toolName, string args) : IScrollItem
{
    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderToolCall(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, toolName, args);
}

/// <summary>工具结果消息项（"  ⎿  " dim 前缀 + 正文）。</summary>
public sealed class ToolResultItem(string text) : IScrollItem
{
    /// <summary>内容高度 = margin 1 + 正文 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>margin 行留空，正文画在 frame.Y+1。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderToolResult(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, text);
}
