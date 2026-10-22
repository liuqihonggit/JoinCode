namespace NativeTui.Core.Repl;

/// <summary>
/// 小终端塌缩进度项 — 终端行数不足以逐条渲染活动工具时的单行替代形态
/// （AgentTool/UI.tsx L469-503 shouldUseCondensedMode：rows &lt; N*9+7 塌缩防闪烁）：
/// margin 1 + 单行 "  ⎿  In progress… · N tool uses"（计数 bold，其余 dim）。
/// 判定 ToolFoldingPolicy.ShouldCondense 由 Driver 消费（终端行数 ITerminalSizeSource 已知通道），
/// ToolUses 可写 — 活动工具计数变化时原地更新（每帧全量重渲自然生效）。
/// </summary>
public sealed class CondensedToolProgressItem(int toolUses) : IScrollItem, ICursorNavigable
{
    /// <summary>内容高度 = margin 1 + 塌缩单行 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>当前活动工具调用计数（Driver 逐事件维护 — ToolCall 加 / ToolResult 减）。</summary>
    public int ToolUses { get; set; } = toolUses;

    /// <summary>渲染：margin 留空 → 塌缩单行（⎿ 前缀 + dim 文案 + bold 计数）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme) =>
        MessageRenderer.RenderCondensedToolProgress(screen, frame with { Y = frame.Y + 1, Height = 1 }, theme, ToolUses);
}
