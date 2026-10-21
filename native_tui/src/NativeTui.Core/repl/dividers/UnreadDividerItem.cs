namespace NativeTui.Core.Repl;

/// <summary>
/// 未读分隔线项（消息列表域）— 对齐基准 Messages.tsx unseen divider（Divider title="N new messages"
/// color=inactive，marginTop=1）：`── N 条新消息 ──` 居中 dim，两侧 ─ 填充全宽；
/// 内容高度 = margin 1 + 线 1（对齐消息项 addMargin 模式）。
/// 挂载/卸载/计数由 ReplView 分界通道管理（MarkUnreadBoundary 快照 → 新消息到达惰性挂载 →
/// 钉底恢复清除 — sticky 滚动跟随状态通道自动驱动）。
/// </summary>
public sealed class UnreadDividerItem(int count) : IScrollItem
{
    /// <summary>分界后未读项计数（ReplView 每帧刷新 — 新消息动态累计，渲染文本随动）。</summary>
    public int Count { get; set; } = count;

    /// <summary>内容高度 = margin 1 + 分隔线 1。</summary>
    public int MeasureHeight(int width) => 2;

    /// <summary>渲染：margin 行留空 → 全宽 ─ dim + 标题 `── N 条新消息 ──` 居中覆盖（宽字符显示宽居中计算）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var row = frame.Y + 1;
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        for (var col = frame.X; col < frame.Right; col++)
        {
            screen[row, col] = new Cell(new Rune('─'), dim, CellWidth.Default);
        }

        var title = $"── {Count} 条新消息 ──";
        var titleWidth = RuneWidth.OfText(title);
        var startCol = frame.X + Math.Max(0, (frame.Width - titleWidth) / 2);
        screen.WriteText(row, startCol, title, dim, frame.Right);
    }
}
