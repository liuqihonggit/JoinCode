namespace NativeTui.Core.Repl;

/// <summary>
/// 子代理分组消息项（M7-7）— 对齐基准 renderGroupedAgentToolUse + AgentProgressLine 渲染结构：
/// margin 1 + 组头 1 行（● 三态色：任一错误 Error 红 / 全完成 Success 绿 / 运行中 Suggestion + 文案）
/// + 每可见代理 2 行（状态行：树形 ├─/└─ + 八色类型名（全同型组内隐藏，空描述兜底显示）+ (描述) +
/// " · N tool uses · M tokens" 统计 dim；活动行："│  ⎿ "/"   ⎿ " 前缀 + lastTool: summary |
/// Initializing… | Done）+ 溢出行（" and N more…" 复用模型选择器形态）。
/// 运行中条目整行 dim（基准 AgentProgressLine isResolved 前提色）；树形收口 └─ 按"组最后一个条目"判定
/// （窗口截断时可见条目均不收口，树延续到溢出行之后语义）。状态源 = SubagentGroupCoordinator（投影，
/// Driver 每事件更新即生效 — 无需失效通知，ScrollBox 每帧全量重渲）。
/// IAnimatable — ReplShell 每帧广播动画时钟，组头 ● 600ms 方波闪烁（基准 ToolUseLoader + useBlink）。
/// </summary>
public sealed class SubagentGroupItem(SubagentGroupCoordinator coordinator) : IScrollItem, IAnimatable
{
    /// <summary>协调器（分组状态的唯一来源 — Driver 事件路由更新）。</summary>
    public SubagentGroupCoordinator Coordinator { get; } = coordinator;

    /// <summary>动画时钟（ReplShell 每帧广播；600ms 方波相位计算）。</summary>
    public long AnimTimeMs { get; set; }

    /// <summary>内容高度 = margin 1 + 组头 1 + 可见条目 ×2 + 溢出 0/1。</summary>
    public int MeasureHeight(int width) =>
        2 + Coordinator.VisibleCount * 2 + (Coordinator.HiddenCount > 0 ? 1 : 0);

    /// <summary>渲染：margin 留空 → 组头（● 闪烁三态 + 文案）→ 可见条目两行式 → 溢出行（如有）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var row = frame.Y + 1;
        MessageRenderer.RenderSubagentHeader(screen, RowFrame(frame, row++), theme, Coordinator.HeaderText, Coordinator.AllComplete, Coordinator.AnyError, AnimTimeMs);

        var hideType = Coordinator.AllSameType;
        for (var i = 0; i < Coordinator.VisibleCount; i++)
        {
            var index = Coordinator.WindowStart + i;
            var entry = Coordinator.Agents[index];
            var isLastOfGroup = index == Coordinator.Count - 1;
            var isRunning = !entry.IsDone;
            MessageRenderer.RenderSubagentStatusRow(screen, RowFrame(frame, row++), theme, isLastOfGroup, entry, isRunning, hideType);
            MessageRenderer.RenderSubagentActivityRow(screen, RowFrame(frame, row++), theme, isLastOfGroup, ActivityText(entry), isRunning);
        }

        if (Coordinator.HiddenCount > 0)
        {
            MessageRenderer.RenderModelOverflow(screen, RowFrame(frame, row), theme, Coordinator.HiddenCount);
        }
    }

    /// <summary>活动行文本 — 完成 Done / 无活动 Initializing… / 最后工具 "名: 摘要"。</summary>
    private static string ActivityText(SubagentEntry entry) =>
        entry.IsDone ? "Done"
        : string.IsNullOrEmpty(entry.LastToolName) ? "Initializing…"
        : $"{entry.LastToolName}: {entry.LastToolSummary}";

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
