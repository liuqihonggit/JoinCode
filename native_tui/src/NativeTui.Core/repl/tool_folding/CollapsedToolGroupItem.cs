namespace NativeTui.Core.Repl;

/// <summary>
/// 工具分组聚合渲染项 — 消费 ToolGroupAggregator 投影（状态唯一来源 = 聚合器，Driver 事件路由更新即生效）：
/// margin 1 + 摘要行 1（● + "读取 N 个文件 · 搜索 M 个模式" + 活动态 "…" 省略号 + (ctrl+o) 展开引导；
/// 完成态整行 dim — 基准 CollapsedReadSearchContent.tsx L448-461 dimColor={!isActiveGroup}）
/// + hint 行 0/1（活动态且非空 — "  ⎿  " + 最新操作路径/模式，700ms 防闪烁由聚合器帧拍结算）。
/// IAnimatable — ReplShell 每帧广播动画时钟：摘要行 ● 600ms 方波闪烁（活动态，基准 ToolUseLoader+useBlink）
/// + OnFrame 推进聚合器 hint 结算（测量/渲染两阶段均幂等调用安全）。
/// </summary>
public sealed class CollapsedToolGroupItem(ToolGroupAggregator aggregator) : IScrollItem, IAnimatable, ICursorNavigable
{
    /// <summary>聚合器（分组状态的唯一来源 — Driver 事件路由更新）。</summary>
    public ToolGroupAggregator Aggregator { get; } = aggregator;

    /// <summary>动画时钟（ReplShell 每帧广播；● 闪烁相位 + hint 700ms 窗口结算）。</summary>
    public long AnimTimeMs { get; set; }

    /// <summary>组活动态（Driver 标记 — 当前回合最后一个聚合组仍在加载；false = 完成定格）。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>hint 行是否渲染 — 活动态且有非空展示 hint（基准 L462 isActiveGroup &amp;&amp; displayedHint）。</summary>
    private bool ShowsHint => IsActive && Aggregator.HintText is { Length: > 0 };

    /// <summary>内容高度 = margin 1 + 摘要行 1 + hint 行 0/1（测量期一并结算 hint 窗口 — 幂等）。</summary>
    public int MeasureHeight(int width)
    {
        Aggregator.OnFrame(AnimTimeMs);
        return 2 + (ShowsHint ? 1 : 0);
    }

    /// <summary>渲染：margin 留空 → 摘要行（● 闪烁 + 文案 + 展开引导）→ hint 行（⎿ 形态，如有）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        Aggregator.OnFrame(AnimTimeMs);
        var row = frame.Y + 1;
        MessageRenderer.RenderCollapsedGroupSummary(screen, RowFrame(frame, row++), theme, Aggregator.SummaryText, IsActive, AnimTimeMs);

        if (Aggregator.HintText is { Length: > 0 } hint && ShowsHint)
        {
            MessageRenderer.RenderToolResult(screen, RowFrame(frame, row), theme, hint);
        }
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
