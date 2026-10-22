namespace NativeTui.Core.Repl;

/// <summary>
/// 后台任务详情对话框消息项（M8-8 登记遗留收口 — 基准 BackgroundTasksDialog.tsx:230-348 详情视图）：
/// 选中态 Enter 挂载的只读详情视图。数据形态 = <b>快照构造</b>（BackgroundTaskEntry + nowMs 一次定格，
/// 不持协调器状态机 — 对比 PermissionRequestItem/ModelPickerDialog 的协调器消费形态：详情无焦点导航/
/// 无选项/无同步阻塞桥，协调器零增益；快照构造更简且天然免疫挂载期间面板 upsert/驱逐扰动）。
/// 渲染结构（对齐权限/模型对话框骨架）：margin 1 + 顶边框（permission 色全宽，复用
/// MessageRenderer.RenderPermissionBorder）+ 标题 "后台任务详情"（1 缩进 bold permission，复用
/// RenderModelTitle）+ 空行 + 字段行组（1 缩进 dim 标签 + 默认样式值 — 任务类别/任务标识/任务描述/
/// 任务状态/终态时间，标签恒 4 汉字 = 显示恒 8 列 + 2 间隔 → 值列恒 col 11，全中文等宽免宽度计算）
/// + 空行 + 底部提示行 dim "esc 返回 · left 关闭"。
/// 任务描述全文折行（复用 PermissionTextWrap.Wrap 词边界折行 — 值区宽 = 帧宽 - 值列起点，
/// 续行缩进对齐值列；空描述折叠为 1 行 "—" 占位）；任务状态汉化复用 BackgroundTaskPanel.StatusText
/// 单一映射源（面板状态段与详情状态行同源不双维护）；终态时间 = FinalizedAtMs&gt;0 时相对秒数
/// "N 秒前"（nowMs 构造注入参数化 — 测试可控防墙钟不确定断言），未终态显示 "—"。
/// 消费方：ReplShell.ShowSelectedBackgroundTask（Enter 挂载 AddMessage）+
/// HandleBackgroundTaskDetailKey（Esc/left 关闭 RemoveDialogItem 移除）。
/// </summary>
public sealed class BackgroundTaskDetailDialog(BackgroundTaskEntry entry, long nowMs) : IScrollItem
{
    /// <summary>标题文案（详情视图标题 — 基准 detail 标题）。</summary>
    private const string TitleText = "后台任务详情";

    /// <summary>底部提示行（esc/left 双关闭键引导 — 基准 Byline 形态对齐 RenderModelShortcuts）。</summary>
    private const string HintText = $"esc 返回{Figures.MiddotSeparator}left 关闭";

    /// <summary>字段标签（恒 4 汉字 = 显示恒 8 列 — 值列对齐无需宽度计算）。</summary>
    private const string KindLabel = "任务类别";
    private const string TaskIdLabel = "任务标识";
    private const string DescriptionLabel = "任务描述";
    private const string StateLabel = "任务状态";
    private const string FinalizedLabel = "终态时间";

    /// <summary>值列起点（1 缩进 + 8 标签列 + 2 间隔 — 字段值与描述续行共用缩进）。</summary>
    private const int ValueIndent = 11;

    /// <summary>详情条目快照（构造定格 — 挂载期间面板 upsert/驱逐不影响已展示内容）。</summary>
    public BackgroundTaskEntry Entry { get; } = entry;

    /// <summary>构造时刻墙钟（终态相对秒数基准 — 测试参数化注入防不确定断言）。</summary>
    public long NowMs { get; } = nowMs;

    /// <summary>内容高度 = margin 1 + 边框 1 + 标题 1 + 空行 1 + 固定字段 4（类别/标识/状态/终态时间）
    /// + 描述行 max(1, N)（空描述折叠 1 行 "—" 占位 — 对齐权限对话框空描述折叠先例）+ 空行 1 + 提示 1。</summary>
    public int MeasureHeight(int width) => 10 + DescriptionLines(width);

    /// <summary>渲染：按固定行序绘制（快照数据 — 无状态流转，与 MeasureHeight 同参数防行数漂移）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderModelTitle(screen, RowFrame(frame, row++), theme, TitleText);
        row++;
        row = RenderField(screen, frame, row, KindLabel, Entry.Kind);
        row = RenderField(screen, frame, row, TaskIdLabel, Entry.TaskId);
        row = RenderDescription(screen, frame, row);
        row = RenderField(screen, frame, row, StateLabel, BackgroundTaskPanel.StatusText(Entry.State));
        row = RenderField(screen, frame, row, FinalizedLabel, FinalizedAtText);
        row++;
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(row, frame.X, $" {HintText}", dim, frame.Right);
    }

    /// <summary>终态时间文案 — FinalizedAtMs&gt;0 时相对秒数 "N 秒前"（TickCount64 差值，
    /// 负差防御钳 0）；未终态 "—"。</summary>
    private string FinalizedAtText => Entry.FinalizedAtMs > 0
        ? $"{Math.Max(0L, (NowMs - Entry.FinalizedAtMs) / 1000)} 秒前"
        : "—";

    /// <summary>描述区渲染 — 首行带标签 + 值首行，续行缩进对齐值列（默认样式 — 字段值区样式统一）。</summary>
    private int RenderDescription(Screen screen, Rect frame, int row)
    {
        var lines = WrapDescription(frame.Width);
        row = RenderField(screen, frame, row, DescriptionLabel, lines.Length > 0 ? lines[0] : "—");
        for (var i = 1; i < lines.Length; i++)
        {
            screen.WriteText(row, frame.X + ValueIndent, lines[i], 0, frame.Right);
            row++;
        }

        return row;
    }

    /// <summary>字段行渲染 — 1 缩进 dim 标签（4 汉字恒 8 列）+ 2 间隔 + 默认样式值（值列 col 11）。</summary>
    private static int RenderField(Screen screen, Rect frame, int row, string label, string value)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(row, frame.X, $" {label}  ", dim, frame.Right);
        screen.WriteText(row, frame.X + ValueIndent, value, 0, frame.Right);
        return row + 1;
    }

    /// <summary>描述折行（值区宽 = 帧宽 - 值列起点；宽度防御钳 1 — 契约破坏不丢内容）。</summary>
    private string[] WrapDescription(int width) =>
        PermissionTextWrap.Wrap(Entry.Description, Math.Max(1, width - ValueIndent));

    /// <summary>描述行数（MeasureHeight/Render 同参数一致性锚点 — 空描述 1 行占位）。</summary>
    private int DescriptionLines(int width) => Math.Max(1, WrapDescription(width).Length);

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
