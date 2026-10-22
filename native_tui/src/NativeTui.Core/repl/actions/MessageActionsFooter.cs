namespace NativeTui.Core.Repl;

/// <summary>
/// 消息操作条状态（消息操作域）— ReplView 渲染前从光标选中项投影（对齐基准 MessageActionsState：
/// msgType/expanded/toolName 的可显示动作集合派生）。CanExpand = enter 展开/收起段显示（分组项）；
/// PrimaryInputLabel = p 复制段显示（主参数映射内工具，null 隐藏 — 基准 applies 过滤语义）。
/// </summary>
/// <param name="CanExpand">选中项可展开/收起（子代理分组 — enter 段显示依据）。</param>
/// <param name="IsExpanded">选中项当前展开态（true = enter 标签"收起"，false = "展开"）。</param>
/// <param name="PrimaryInputLabel">主参数标签（p 段 "复制 {label}" 文案；null = 不显示 p 段）。</param>
public sealed record MessageActionsState(bool CanExpand, bool IsExpanded, string? PrimaryInputLabel)
{
    /// <summary>从消息项投影操作条状态（null = 无项 — 操作条不渲染）。
    /// enter 段：子代理分组项（基准 grouped_tool_use 展开语义）；p 段：主参数映射内工具调用项。</summary>
    public static MessageActionsState? Of(IScrollItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var primaryInputLabel = item is IToolCallInfo { ToolName: var toolName } && PrimaryInputMap.Supports(toolName)
            ? PrimaryInputMap.LabelOf(toolName)
            : null;
        return new MessageActionsState(
            CanExpand: item is SubagentGroupItem,
            IsExpanded: item is SubagentGroupItem { Coordinator.IsExpanded: true },
            PrimaryInputLabel: primaryInputLabel);
    }
}

/// <summary>
/// 底部消息操作条（消息操作域）— 对齐基准 messageActions.tsx MessageActionsFooter：光标态激活时渲染在
/// footer 上方（基准 bottom 区 PromptInput 下方 — 本项目光标态建议区恒空，位置等价）：
/// 上边框 1 行 dim ─（borderTop borderDimColor — 与输入框 ─ 线同形态，footer 高度稳定语义）
/// + 内容 1 行（paddingX=2 由组装层 frame.X=2 承载，对齐 FooterBar 模式）：
/// 动作段序列 `key bold + ' ' label dim`、段间 " · " dim、尾随 ↑↓ 导航 / esc 返回 恒显段。
/// </summary>
public sealed class MessageActionsFooter
{
    /// <summary>操作条高度（上边框 1 + 内容 1）。</summary>
    public const int Height = 2;

    /// <summary>渲染操作条（frame 高度须 ≥ Height — 行 0 边框、行 1 内容）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme, MessageActionsState state)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        RenderBorder(screen, frame, dim);
        RenderActions(screen, frame with { Y = frame.Y + 1, Height = 1 }, state, dim);
    }

    /// <summary>上边框行 — 全宽 dim ─（基准 borderStyle single borderTop borderDimColor）。</summary>
    private static void RenderBorder(Screen screen, Rect frame, int dim)
    {
        for (var col = frame.X; col < frame.Right; col++)
        {
            screen[frame.Y, col] = new Cell(new Rune('─'), dim, CellWidth.Default);
        }
    }

    /// <summary>内容行 — 动作段序列（key bold + 空格 label dim，段间 " · " dim — 逐段写入裁剪走 rightBound）。</summary>
    private static void RenderActions(Screen screen, Rect frame, MessageActionsState state, int dim)
    {
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        var col = frame.X;
        var first = true;
        foreach (var (key, label) in SegmentsOf(state))
        {
            if (!first)
            {
                col = screen.WriteText(frame.Y, col, Figures.MiddotSeparator, dim, frame.Right);
            }

            first = false;
            col = screen.WriteText(frame.Y, col, key, bold, frame.Right);
            col = screen.WriteText(frame.Y, col, $" {label}", dim, frame.Right);
        }
    }

    /// <summary>动作段序列（enter 展开段可条件隐藏 + c 复制恒显 + p 主参数段可条件隐藏 + ↑↓ 导航 / esc 返回 恒显）。</summary>
    private static IEnumerable<(string Key, string Label)> SegmentsOf(MessageActionsState state)
    {
        if (state.CanExpand)
        {
            yield return ("enter", state.IsExpanded ? "收起" : "展开");
        }

        yield return ("c", "复制");
        if (state.PrimaryInputLabel is { } label)
        {
            yield return ("p", $"复制 {label}");
        }

        yield return ($"{Figures.UpArrow}{Figures.DownArrow}", "导航");
        yield return ("esc", "返回");
    }
}
