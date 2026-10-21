namespace NativeTui.Core.Repl;

/// <summary>
/// 后台任务条目（M8-8）— 常驻面板单行渲染状态投影（不可变 record，Upsert 替换式更新）。
/// State 直通存储协议原串（running|completed|failed|cancelled — 渲染层映射汉化文案与色，
/// 未知值宽容原样显示；不引入枚举双源 — record 须保留原始串支持协议演进）。
/// </summary>
/// <param name="Kind">任务类别（协议值 fork|teammate — 行内原样显示）。</param>
/// <param name="TaskId">任务标识（与 Kind 组成复合 upsert 键）。</param>
/// <param name="Description">任务描述（行正文）。</param>
/// <param name="State">任务状态（协议原串 — 渲染层映射，未知值宽容）。</param>
public sealed record BackgroundTaskEntry(string Kind, string TaskId, string Description, string State);

/// <summary>
/// 后台任务常驻面板（M8-8 TASK064 末项首版）— 对齐基准 CoordinatorAgentStatus.tsx:34-76 首版简化汉化：
/// 输入区上方排队面板同位的只读常驻面板（每任务一行 "● kind: 描述 · 状态"），
/// upsert 语义（按 (kind,taskId) 更新/插入 — SubagentGroupCoordinator 先例的独立协调器形态，
/// 区别：不落滚动区、终态驻留不随回合清理）、上限窗口 min(10, n) + "+N more" 溢出行
/// （基准全量渲染靠 React 虚拟化扛 300 规模，本项目无虚拟化 — 窗口+提示为对等机制，
/// 对齐 MaxQueuedInputs=10 / SubagentGroupCoordinator.MaxVisible 先例）。
/// 首版只读显示无停止交互；evictAfter 过期驱逐下批做。
/// 消费方：ReplBridgeDriver 路由（BackgroundTask 事件）+ ReplView 渲染（排队面板之上）。
/// </summary>
public sealed class BackgroundTaskPanel
{
    /// <summary>最大可见条数（对齐 MaxQueuedInputs=10 / SubagentGroupCoordinator.MaxVisible 先例 — 防 300 规模刷屏）。</summary>
    public const int MaxVisible = 10;

    private readonly List<BackgroundTaskEntry> _tasks = [];

    /// <summary>任务条目清单（插入序 — 渲染窗口取前 N 条，断言投影）。</summary>
    public IReadOnlyList<BackgroundTaskEntry> Tasks => _tasks;

    /// <summary>可见条数 = min(10, n)。</summary>
    public int VisibleCount => Math.Min(MaxVisible, _tasks.Count);

    /// <summary>窗口外条数（"+N more" 溢出行依据）。</summary>
    public int HiddenCount => Math.Max(0, _tasks.Count - VisibleCount);

    /// <summary>面板高度 — 空面板 0（对齐 SpinnerItem.Hidden 空态零渲染先例）；超窗 +1 行溢出提示。</summary>
    public int MeasureHeight() => _tasks.Count == 0 ? 0 : VisibleCount + (HiddenCount > 0 ? 1 : 0);

    /// <summary>upsert — 按 (kind,taskId) 复合键查找：存在替换更新（描述/状态），不存在插入（终态驻留 — 首版不驱逐）。</summary>
    public void Upsert(string kind, string taskId, string description, string state)
    {
        var index = _tasks.FindIndex(task => task.Kind == kind && task.TaskId == taskId);
        var updated = new BackgroundTaskEntry(kind, taskId, description, state);
        if (index < 0)
        {
            _tasks.Add(updated);
            return;
        }

        _tasks[index] = updated;
    }

    /// <summary>渲染 — 每任务一行 "● kind: 描述 · 状态"：● 状态色（running text / completed success /
    /// failed、cancelled error）、kind+描述 normal、状态 dim；超窗末行 "+N more" dim；空面板零输出。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        for (var i = 0; i < VisibleCount; i++)
        {
            var task = _tasks[i];
            var row = frame.Y + i;
            var dotStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = DotColor(task.State, theme) });
            var col = screen.WriteText(row, frame.X, $"{Figures.BlackCircle} ", dotStyle, frame.Right);
            col = screen.WriteText(row, col, $"{task.Kind}: {task.Description}", styleId: 0, rightBound: frame.Right);
            screen.WriteText(row, col, $"{Figures.MiddotSeparator}{StatusText(task.State)}", dim, frame.Right);
        }

        if (HiddenCount > 0)
        {
            screen.WriteText(frame.Y + VisibleCount, frame.X, $"+{HiddenCount} more", dim, frame.Right);
        }
    }

    /// <summary>状态文案汉化（running 运行中 / completed 已完成 / failed 失败 / cancelled 已取消；未知值原样显示 — 协议演进宽容）。</summary>
    private static string StatusText(string state) => state switch
    {
        "running" => "运行中",
        "completed" => "已完成",
        "failed" => "失败",
        "cancelled" => "已取消",
        _ => state,
    };

    /// <summary>● 状态色（running theme.Text / completed theme.Success / failed、cancelled theme.Error；未知值同 running）。</summary>
    private static Color DotColor(string state, Theme theme) => state switch
    {
        "completed" => theme.Success,
        "failed" or "cancelled" => theme.Error,
        _ => theme.Text,
    };
}
