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
/// <param name="FinalizedAtMs">终态定格时间戳（Environment.TickCount64；0 = 非终态 —
/// M8-8 后续 evictAfter 驱逐依据：终态条目超时移除，对齐基准 CoordinatorAgentStatus.tsx:51-63）。</param>
public sealed record BackgroundTaskEntry(string Kind, string TaskId, string Description, string State, long FinalizedAtMs = 0);

/// <summary>
/// 后台任务常驻面板（M8-8 TASK064 末项首版 + M8-8 后续停止交互/驱逐）— 对齐基准
/// CoordinatorAgentStatus.tsx:34-76 首版简化汉化 + :51-63 evictAfter 驱逐：
/// 输入区上方排队面板同位的常驻面板（每任务一行 "● kind: 描述 · 状态"），
/// upsert 语义（按 (kind,taskId) 更新/插入 — SubagentGroupCoordinator 先例的独立协调器形态，
/// 区别：不落滚动区、终态驻留 5s 后过期驱逐）、上限窗口 min(10, n) + "+N more" 溢出行
/// （基准全量渲染靠 React 虚拟化扛 300 规模，本项目无虚拟化 — 窗口+提示为对等机制，
/// 对齐 MaxQueuedInputs=10 / SubagentGroupCoordinator.MaxVisible 先例）。
/// 选中态（M8-8 登记遗留 — Shift+Down 进入 / ↑↓kj 移动 / Enter 查看 / x 停止 / Esc 退出，
/// 键位接线在 ReplShell）：仅 running 条目可选（终态不可选 — 驱逐倒计时中无操作意义），
/// 选中导航窗口跟随（WindowStart — 对齐 ModelPicker.MoveFocus 先例，选中行恒可见）。
/// 消费方：ReplBridgeDriver 路由（BackgroundTask 事件）+ ReplView 渲染（排队面板之上，
/// Render 内每帧 TickEvict 驱逐）+ ReplShell 选中导航模态（IsSelectionActive/MoveSelection/
/// SelectedEntry/HasRunning）。
/// </summary>
public sealed class BackgroundTaskPanel
{
    /// <summary>最大可见条数（对齐 MaxQueuedInputs=10 / SubagentGroupCoordinator.MaxVisible 先例 — 防 300 规模刷屏）。</summary>
    public const int MaxVisible = 10;

    /// <summary>终态驱逐时长（ms — 基准 CoordinatorAgentStatus evictAfter 5s：终态条目驻留 5s 供用户确认结果后移除）。</summary>
    public const int EvictAfterMs = 5000;

    /// <summary>运行中状态（协议值 — 内部判定统一引用，防字面量散落）。</summary>
    public const string RunningState = "running";

    /// <summary>成功终态（协议值）。</summary>
    public const string CompletedState = "completed";

    /// <summary>失败终态（协议值）。</summary>
    public const string FailedState = "failed";

    /// <summary>取消终态（协议值）。</summary>
    public const string CancelledState = "cancelled";

    /// <summary>面板级命中 id（TASK066 板块 C 基建 C2 接线 — 命中语义：面板空白区域/溢出行
    /// "+N more" 点击吞掉不启动文本选区，面板为 UI 交互区对齐基准 ink Box 包裹行区域的组件形态
    /// CoordinatorAgentStatus.tsx:72-75；行级 id 见 <see cref="HitIdOf"/> — 前缀相异无歧义）。</summary>
    public const string PanelHitId = "background-task-panel";

    private readonly List<BackgroundTaskEntry> _tasks = [];

    /// <summary>任务条目清单（插入序 — 渲染窗口取前 N 条，断言投影）。</summary>
    public IReadOnlyList<BackgroundTaskEntry> Tasks => _tasks;

    /// <summary>可见条数 = min(10, n)。</summary>
    public int VisibleCount => Math.Min(MaxVisible, _tasks.Count);

    /// <summary>窗口外条数（"+N more" 溢出行依据）。</summary>
    public int HiddenCount => Math.Max(0, _tasks.Count - VisibleCount);

    /// <summary>面板高度 — 空面板 0（对齐 SpinnerItem.Hidden 空态零渲染先例）；超窗 +1 行溢出提示。</summary>
    public int MeasureHeight() => _tasks.Count == 0 ? 0 : VisibleCount + (HiddenCount > 0 ? 1 : 0);

    /// <summary>是否存在运行中条目（M8-8 后续 x 停止键门槛 — 无 running 时 x 无操作走字符插入）。</summary>
    public bool HasRunning => _tasks.Exists(static task => task.State == RunningState);

    /// <summary>选中索引（-1 = 无选中；仅指向 running 条目 — 终态不可选，MoveSelection 跳过终态；
    /// 驱逐/Upsert 状态翻转后不主动校正，SelectedEntry 守卫兜底）。</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>选中态激活（M8-8 登记遗留 — SelectedIndex >= 0 语义糖，ReplShell 选中导航模态分派依据）。</summary>
    public bool IsSelectionActive => SelectedIndex >= 0;

    /// <summary>滚动窗口起点（0 起 — 选中导航窗口跟随，对齐 ModelPicker.WindowStart 先例：
    /// MoveSelection 保证选中行可见的最小窗口位移；常态恒 0，ClearSelection 归零回默认视图）。</summary>
    public int WindowStart { get; private set; }

    /// <summary>选中条目（M8-8 后续 — 返回 (Kind,TaskId) 元组；仅 running 态可选：无选中或选中条目
    /// 已翻终态返回 null — 停止键消费方回退首个 running 条目）。</summary>
    public (string Kind, string TaskId)? SelectedEntry =>
        SelectedIndex is >= 0 and var index && index < _tasks.Count && _tasks[index].State == RunningState
            ? (_tasks[index].Kind, _tasks[index].TaskId)
            : null;

    /// <summary>upsert — 按 (kind,taskId) 复合键查找：存在替换更新，不存在插入（终态 upsert 允许直插 —
    /// 快速完成 fork 的完成事件先于任何 running upsert 到达时留痕）。
    /// 空描述保留旧值（快照/停止路径 description 恒空串 — 防清屏；首插无旧值可保直存空串）。
    /// 终态定格时间戳：非终态→终态首翻写入墙钟；终态幂等重发保留首个（防快照重发无限续期驻留）。</summary>
    public void Upsert(string kind, string taskId, string description, string state)
    {
        var index = _tasks.FindIndex(task => task.Kind == kind && task.TaskId == taskId);
        var finalizedAtMs = IsFinal(state) ? Environment.TickCount64 : 0;
        if (index < 0)
        {
            _tasks.Add(new BackgroundTaskEntry(kind, taskId, description, state, finalizedAtMs));
            return;
        }

        var existing = _tasks[index];
        var mergedDescription = description.Length > 0 ? description : existing.Description;
        if (IsFinal(state) && IsFinal(existing.State))
        {
            finalizedAtMs = existing.FinalizedAtMs;
        }

        _tasks[index] = new BackgroundTaskEntry(kind, taskId, mergedDescription, state, finalizedAtMs);
    }

    /// <summary>移动选中（M8-8 后续 — delta ±1 沿方向找最近 running 条目，跳过终态条目；
    /// 无选中时按 delta 方向取首个 running（向前从条目 0、向后从末条）；无 running 或到头保持不变
    /// （不环绕 — 对齐 ModelPicker.MoveFocus clamp 列表语义）；命中后窗口跟随保证选中行可见）。
    /// 消费方：ReplShell 选中导航模态 ↑↓/kj 键 + Shift+Down 进入（无选中 → 首个 running）。</summary>
    /// <param name="delta">移动方向与步数符号（+1 向后 / -1 向前）</param>
    public void MoveSelection(int delta)
    {
        var count = _tasks.Count;
        if (count == 0)
        {
            return;
        }

        var start = SelectedIndex is >= 0 and var index && index < count
            ? index
            : (delta > 0 ? -1 : count);
        var step = delta > 0 ? 1 : -1;
        for (var i = start + step; i >= 0 && i < count; i += step)
        {
            if (_tasks[i].State == RunningState)
            {
                SelectedIndex = i;
                FollowWindowToSelection();
                return;
            }
        }
    }

    /// <summary>退出选中态（M8-8 登记遗留 — ReplShell 选中导航模态 Esc 消费）：
    /// SelectedIndex 归 -1 + 窗口归 0（回默认视图 — 对齐 ModelPicker.Complete 归零先例，
    /// 常驻面板常态展示插入序前 N 条）。</summary>
    public void ClearSelection()
    {
        SelectedIndex = -1;
        WindowStart = 0;
    }

    /// <summary>行级命中 id 构造（TASK066 C2 — 单一构造源防 id 双维护）：注册侧
    /// <see cref="RegisterHitTargets"/> 与消费侧 <see cref="TrySelectByHitId"/> 共用完整字符串
    /// 比对（免解析歧义 — taskId 含冒号也安全），格式「域:类别:键」对齐 IScrollItem.HitTestId
    /// 命名建议（ScrollBox.cs:15 — background-task:fork:42 形态）。</summary>
    /// <param name="kind">任务类别（协议值 fork|teammate）。</param>
    /// <param name="taskId">任务标识。</param>
    internal static string HitIdOf(string kind, string taskId) => $"background-task:{kind}:{taskId}";

    /// <summary>命中标注（TASK066 C2 接线 — ReplView.Render 在面板 Render 后调用，空面板零渲染
    /// 时不调用即零标注）：面板级整块 rect 先注册（覆盖含溢出行 — 行级后注册覆盖之，HitTest
    /// 反向遍历行级优先 = 后绘制兄弟顶层优先，对齐基准 hit-test.ts:33-39）+ 每可见行 1 行高 rect
    ///（行 y = frame.Y + i 与 <see cref="Render"/> 行偏移同公式 — 行高恒 1 无 wrap 公式稳定；
    /// 溢出行/空白无行级标注，命中回落面板级 id）。坐标为屏幕坐标（rect 已是平移后位置 —
    /// 对齐基准 nodeCache 屏幕坐标语义 hit-test.ts:9-10）。</summary>
    /// <param name="registry">命中注册表（ReplView 帧生命周期持有）。</param>
    /// <param name="frame">面板屏幕 rect（ReplView 渲染时传入）。</param>
    public void RegisterHitTargets(HitTestRegistry registry, Rect frame)
    {
        registry.Register(PanelHitId, frame.Y, frame.X, frame.Width, frame.Height);
        for (var i = 0; i < VisibleCount; i++)
        {
            var task = _tasks[WindowStart + i];
            registry.Register(HitIdOf(task.Kind, task.TaskId), frame.Y + i, frame.X, frame.Width, 1);
        }
    }

    /// <summary>命中行选中（TASK066 C2 消费 — ReplShell 单击分派）：按完整行 id 匹配可见行
    ///（窗口外行无 rect 天然不命中，防御仍限定可见范围）→ 直设 SelectedIndex + 窗口跟随；
    /// 终态行可选中（对齐基准 AgentLine 任意状态 onClick 均可查看 CoordinatorAgentStatus.tsx:74；
    /// 终态选中后的 x 停止由 <see cref="SelectedEntry"/> 终态守卫回退首个 running 兜底）；
    /// 不走 <see cref="MoveSelection"/> 跳终态 — 点击是直接定位非相邻导航。未匹配返回 false
    ///（非本面板 id — 调用方回落既有链）。</summary>
    /// <param name="hitId">命中表返回的 id（HandleMouseEvent 单击查询）。</param>
    /// <returns>true = 本面板行命中并已选中；false = 非本面板 id。</returns>
    internal bool TrySelectByHitId(string hitId)
    {
        for (var i = 0; i < VisibleCount; i++)
        {
            var itemIndex = WindowStart + i;
            var task = _tasks[itemIndex];
            if (hitId == HitIdOf(task.Kind, task.TaskId))
            {
                SelectedIndex = itemIndex;
                FollowWindowToSelection();
                return true;
            }
        }

        return false;
    }

    /// <summary>窗口跟随（对齐 ModelPicker.MoveFocus 先例）— 保证选中索引可见的最小窗口位移。</summary>
    private void FollowWindowToSelection()
    {
        if (SelectedIndex < WindowStart)
        {
            WindowStart = SelectedIndex;
        }
        else if (SelectedIndex >= WindowStart + VisibleCount)
        {
            WindowStart = SelectedIndex - VisibleCount + 1;
        }
    }

    /// <summary>终态驱逐（M8-8 后续 evictAfter — 基准 CoordinatorAgentStatus.tsx:51-63）：
    /// 终态条目定格超 <paramref name="evictAfterMs"/> 秒移除（仅删终态 — running 永不驱逐；
    /// FinalizedAtMs=0 非终态不受影响）；选中索引随移除位平移/钳制（选中位被删置 -1、
    /// 后移条目索引前移、超界钳到末位；-1 不动）+ 滚动窗口钳制（防删除后悬空）。
    /// 全终态+全驱逐后面板回到零渲染。
    /// 调用方：ReplView.Render 每帧拍（墙钟直取，对齐 spinner WallNowMs 先例）；测试传可控时间。</summary>
    /// <param name="nowMs">当前墙钟（Environment.TickCount64 — 测试可传偏移值控制过期）</param>
    /// <param name="evictAfterMs">终态驻留时长（默认 <see cref="EvictAfterMs"/>）</param>
    public void TickEvict(long nowMs, int evictAfterMs = EvictAfterMs)
    {
        for (var i = _tasks.Count - 1; i >= 0; i--)
        {
            var entry = _tasks[i];
            if (entry.FinalizedAtMs <= 0 || nowMs - entry.FinalizedAtMs < evictAfterMs)
            {
                continue;
            }

            _tasks.RemoveAt(i);
            if (SelectedIndex == i)
            {
                SelectedIndex = -1;
            }
            else if (SelectedIndex > i)
            {
                SelectedIndex--;
            }
        }

        if (SelectedIndex >= _tasks.Count)
        {
            SelectedIndex = _tasks.Count - 1;
        }

        var maxWindowStart = Math.Max(0, _tasks.Count - VisibleCount);
        if (WindowStart > maxWindowStart)
        {
            WindowStart = maxWindowStart;
        }
    }

    /// <summary>渲染 — 每任务一行 "● kind: 描述 · 状态"：● 状态色（running text / completed success /
    /// failed、cancelled error）、kind+描述 normal、状态 dim；超窗末行 "+N more" dim；空面板零输出。
    /// 选中行（M8-8 登记遗留）— 对齐 ModelPicker.RenderModelListRow 焦点形态：❯ 前缀（suggestion 色）
    /// 替换 ● 状态点 + 正文段 suggestion 色（状态文字信息由行尾状态段保留）；渲染循环按 WindowStart
    /// 偏移取条目（选中导航窗口跟随）。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var selection = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion });
        for (var i = 0; i < VisibleCount; i++)
        {
            var itemIndex = WindowStart + i;
            var task = _tasks[itemIndex];
            var row = frame.Y + i;
            var selected = itemIndex == SelectedIndex;
            var col = selected
                ? screen.WriteText(row, frame.X, $"{Figures.Pointer} ", selection, frame.Right)
                : screen.WriteText(row, frame.X, $"{Figures.BlackCircle} ", screen.Styles.GetOrAdd(new TextStyle { Foreground = DotColor(task.State, theme) }), frame.Right);
            col = screen.WriteText(row, col, $"{task.Kind}: {task.Description}", styleId: selected ? selection : 0, rightBound: frame.Right);
            screen.WriteText(row, col, $"{Figures.MiddotSeparator}{StatusText(task.State)}", dim, frame.Right);
        }

        if (HiddenCount > 0)
        {
            screen.WriteText(frame.Y + VisibleCount, frame.X, $"+{HiddenCount} more", dim, frame.Right);
        }
    }

    /// <summary>终态判定（completed/failed/cancelled — 选中守卫/驱逐/时间戳三处共用；未知值按非终态宽容处理）。</summary>
    internal static bool IsFinal(string state) =>
        state is CompletedState or FailedState or CancelledState;

    /// <summary>状态文案汉化（running 运行中 / completed 已完成 / failed 失败 / cancelled 已取消；未知值原样显示 — 协议演进宽容）。
    /// 消费方：本面板 Render 状态段 + BackgroundTaskDetailDialog 详情状态行（internal — 单一映射源不双维护）。</summary>
    internal static string StatusText(string state) => state switch
    {
        RunningState => "运行中",
        CompletedState => "已完成",
        FailedState => "失败",
        CancelledState => "已取消",
        _ => state,
    };

    /// <summary>● 状态色（running theme.Text / completed theme.Success / failed、cancelled theme.Error；未知值同 running）。</summary>
    private static Color DotColor(string state, Theme theme) => state switch
    {
        CompletedState => theme.Success,
        FailedState or CancelledState => theme.Error,
        _ => theme.Text,
    };
}
