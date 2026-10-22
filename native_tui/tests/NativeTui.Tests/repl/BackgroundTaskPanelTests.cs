namespace NativeTui.Tests.Repl;

/// <summary>
/// 后台任务常驻面板测试（M8-8 TASK064 末项首版）— 对齐基准 CoordinatorAgentStatus.tsx:34-76 首版简化汉化：
/// upsert 语义（(kind,taskId) 复合键更新/插入）、每任务一行渲染（● 状态色 + kind:描述 + dim 状态）、
/// 终态驻留（completed/failed/cancelled 不驱逐）、空面板零渲染（MeasureHeight=0 — SpinnerItem.Hidden 先例）、
/// 上限窗口 min(10,n) + "+N more" 溢出（MaxQueuedInputs=10 先例）、ReplView 挂载位置（排队面板之上 — 输入区上方）。
/// </summary>
public sealed class BackgroundTaskPanelTests
{
    [Fact]
    public void Upsert_NewTask_InsertsEntry()
    {
        var panel = new BackgroundTaskPanel();

        panel.Upsert("fork", "fork-123", "检查README", "running");

        var task = Assert.Single(panel.Tasks);
        Assert.Equal("fork", task.Kind);
        Assert.Equal("fork-123", task.TaskId);
        Assert.Equal("检查README", task.Description);
        Assert.Equal("running", task.State);
        Assert.Equal(1, panel.MeasureHeight());
    }

    [Fact]
    public void Upsert_ExistingKey_UpdatesStateAndDescription()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "fork-123", "检查README", "running");

        panel.Upsert("fork", "fork-123", "检查README与文档", "completed");

        Assert.Single(panel.Tasks);
        Assert.Equal("completed", panel.Tasks[0].State);
        Assert.Equal("检查README与文档", panel.Tasks[0].Description);
    }

    [Fact]
    public void Upsert_SameTaskIdWithDifferentKind_AreSeparateEntries()
    {
        var panel = new BackgroundTaskPanel();

        panel.Upsert("fork", "task-1", "fork 任务", "running");
        panel.Upsert("teammate", "task-1", "teammate 任务", "running");

        Assert.Equal(2, panel.Tasks.Count);
        Assert.Equal(2, panel.MeasureHeight());
    }

    [Fact]
    public void TerminalStates_StayInPanel()
    {
        var panel = new BackgroundTaskPanel();

        panel.Upsert("fork", "fork-1", "检查", "completed");
        panel.Upsert("fork", "fork-2", "构建", "failed");
        panel.Upsert("teammate", "tm-1", "审阅", "cancelled");

        // 终态驻留 — 驱逐由 TickEvict 驱动（默认 5s 后过期移除，非立即）
        Assert.Equal(3, panel.Tasks.Count);
    }

    /// <summary>终态 upsert 首翻写入 FinalizedAtMs，幂等重发保留首个（防快照重发无限续期驻留）。</summary>
    [Fact]
    public void Upsert_FinalState_RecordsFinalizedTimestamp_IdempotentRerefreshKeepsFirst()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "fork-1", "检查", "running");
        Assert.Equal(0, panel.Tasks[0].FinalizedAtMs);

        panel.Upsert("fork", "fork-1", "检查", "completed");
        var firstFinalized = panel.Tasks[0].FinalizedAtMs;
        Assert.True(firstFinalized > 0);

        // 终态幂等重发（快照路径）不刷新时间戳 — 驱逐倒计时不重置
        panel.Upsert("fork", "fork-1", "检查", "completed");
        Assert.Equal(firstFinalized, panel.Tasks[0].FinalizedAtMs);
    }

    /// <summary>空描述保留旧值（快照/停止路径 description 恒空串 — 防清屏；非空描述正常更新）。</summary>
    [Fact]
    public void Upsert_EmptyDescription_PreservesExistingDescription()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "fork-1", "检查README", "running");

        panel.Upsert("fork", "fork-1", "", "completed");

        Assert.Equal("检查README", panel.Tasks[0].Description);
        Assert.Equal("completed", panel.Tasks[0].State);
    }

    /// <summary>MoveSelection 无选中 → 向前取首个 running 条目。</summary>
    [Fact]
    public void MoveSelection_NoSelection_ForwardTakesFirstRunning()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "f-1", "任务1", "running");
        panel.Upsert("fork", "f-2", "任务2", "running");

        panel.MoveSelection(1);

        Assert.Equal(0, panel.SelectedIndex);
        Assert.Equal(("fork", "f-1"), panel.SelectedEntry);
    }

    /// <summary>MoveSelection 跳过终态条目 — 仅 running 态条目可选。</summary>
    [Fact]
    public void MoveSelection_SkipsFinalEntries_OnlyRunningSelectable()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "f-1", "任务1", "completed");
        panel.Upsert("fork", "f-2", "任务2", "running");
        panel.Upsert("fork", "f-3", "任务3", "failed");
        panel.Upsert("fork", "f-4", "任务4", "running");

        panel.MoveSelection(1);

        // f-1 终态跳过 → 选中首个 running f-2
        Assert.Equal(1, panel.SelectedIndex);
        Assert.Equal(("fork", "f-2"), panel.SelectedEntry);

        // 再向后移：f-3 终态跳过 → f-4
        panel.MoveSelection(1);
        Assert.Equal(3, panel.SelectedIndex);
        Assert.Equal(("fork", "f-4"), panel.SelectedEntry);
    }

    /// <summary>MoveSelection 到头不动（不环绕）；反向同理。</summary>
    [Fact]
    public void MoveSelection_AtEdge_StaysPut()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "f-1", "任务1", "running");
        panel.MoveSelection(1);
        Assert.Equal(0, panel.SelectedIndex);

        panel.MoveSelection(-1);
        Assert.Equal(0, panel.SelectedIndex);
        panel.MoveSelection(1);
        Assert.Equal(0, panel.SelectedIndex);
    }

    /// <summary>SelectedEntry 终态守卫 — 选中条目翻终态后返回 null（停止键消费方回退首个 running）。</summary>
    [Fact]
    public void SelectedEntry_WhenSelectedTurnsFinal_ReturnsNull()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "f-1", "任务1", "running");
        panel.MoveSelection(1);
        Assert.NotNull(panel.SelectedEntry);

        panel.Upsert("fork", "f-1", "任务1", "cancelled");

        Assert.Null(panel.SelectedEntry);
        Assert.Equal(0, panel.SelectedIndex);
    }

    /// <summary>TickEvict 驱逐窗口内终态驻留（running 永不驱逐）。</summary>
    [Fact]
    public void TickEvict_WithinWindow_FinalStateStays_RunningNeverEvicted()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "f-1", "任务1", "running");
        panel.Upsert("fork", "f-2", "任务2", "completed");
        var finalizedAt = panel.Tasks[1].FinalizedAtMs;

        // 5s 窗口内（4999ms）终态驻留
        panel.TickEvict(finalizedAt + BackgroundTaskPanel.EvictAfterMs - 1);

        Assert.Equal(2, panel.Tasks.Count);
        Assert.Equal("running", panel.Tasks[0].State);
        Assert.Equal("completed", panel.Tasks[1].State);
    }

    /// <summary>TickEvict 超时驱逐终态条目（running 不驱逐 — 全终态+全驱逐后面板回到零渲染）。</summary>
    [Fact]
    public void TickEvict_AfterWindow_FinalStateEvicted()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "f-1", "任务1", "running");
        panel.Upsert("fork", "f-2", "任务2", "completed");
        panel.Upsert("teammate", "tm-1", "审阅", "failed");
        var finalizedAt = panel.Tasks[1].FinalizedAtMs;

        panel.TickEvict(finalizedAt + BackgroundTaskPanel.EvictAfterMs);

        // 两终态条目驱逐，running 条目保留
        var remaining = Assert.Single(panel.Tasks);
        Assert.Equal(("fork", "f-1", "running"), (remaining.Kind, remaining.TaskId, remaining.State));

        // 全终态 + 全驱逐 → 零渲染
        panel.Upsert("fork", "f-1", "任务1", "cancelled");
        panel.TickEvict(panel.Tasks[0].FinalizedAtMs + BackgroundTaskPanel.EvictAfterMs);
        Assert.Empty(panel.Tasks);
        Assert.Equal(0, panel.MeasureHeight());
    }

    /// <summary>TickEvict 选中索引钳制 — 选中位被删置 -1，后续条目前移索引平移，超界钳到末位。</summary>
    [Fact]
    public void TickEvict_SelectionClamped_WhenEntriesEvicted()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "f-1", "任务1", "completed");
        panel.Upsert("fork", "f-2", "任务2", "running");
        panel.Upsert("fork", "f-3", "任务3", "running");
        panel.MoveSelection(1);
        panel.MoveSelection(1);
        Assert.Equal(2, panel.SelectedIndex);
        Assert.Equal(("fork", "f-3"), panel.SelectedEntry);

        // f-1 终态驱逐 — 选中位 2 > 移除位 0 → 前移为 1（仍指向 f-3）
        panel.TickEvict(panel.Tasks[0].FinalizedAtMs + BackgroundTaskPanel.EvictAfterMs);
        Assert.Equal(1, panel.SelectedIndex);
        Assert.Equal(("fork", "f-3"), panel.SelectedEntry);

        // 选中位自身被删（f-3 翻终态后驱逐）→ 置 -1
        panel.Upsert("fork", "f-3", "任务3", "cancelled");
        panel.TickEvict(panel.Tasks[1].FinalizedAtMs + BackgroundTaskPanel.EvictAfterMs);
        Assert.Equal(-1, panel.SelectedIndex);
        Assert.Null(panel.SelectedEntry);
    }

    /// <summary>HasRunning 门槛 — 存在 running 条目为 true，全终态/空面板为 false。</summary>
    [Fact]
    public void HasRunning_ReflectsRunningPresence()
    {
        var panel = new BackgroundTaskPanel();
        Assert.False(panel.HasRunning);

        panel.Upsert("fork", "f-1", "任务1", "completed");
        Assert.False(panel.HasRunning);

        panel.Upsert("teammate", "tm-1", "审阅", "running");
        Assert.True(panel.HasRunning);

        panel.Upsert("teammate", "tm-1", "审阅", "cancelled");
        Assert.False(panel.HasRunning);
    }

    [Fact]
    public void EmptyPanel_MeasuresZeroHeight()
    {
        var panel = new BackgroundTaskPanel();

        Assert.Empty(panel.Tasks);
        Assert.Equal(0, panel.VisibleCount);
        Assert.Equal(0, panel.HiddenCount);
        Assert.Equal(0, panel.MeasureHeight());
    }

    [Fact]
    public void OverLimit_WindowPlusMoreHintRow()
    {
        var panel = new BackgroundTaskPanel();
        for (var i = 1; i <= 11; i++)
        {
            panel.Upsert("fork", $"fork-{i}", $"任务{i}", "running");
        }

        Assert.Equal(11, panel.Tasks.Count);
        Assert.Equal(10, panel.VisibleCount);
        Assert.Equal(1, panel.HiddenCount);
        // 10 条可见行 + 1 行 "+1 more" 溢出提示
        Assert.Equal(11, panel.MeasureHeight());
    }

    [Fact]
    public void Render_RunningTask_TextColorDotAndDimStatus()
    {
        using var screen = new Screen(cols: 40, rows: 1);
        var panel = new BackgroundTaskPanel();

        panel.Upsert("fork", "fork-123", "检查README", "running");
        panel.Render(screen, new Rect(0, 0, 40, 1), Theme.Dark);

        Assert.Equal("● fork: 检查README · 运行中", RowText.Of(screen, 0, 0, 40).TrimEnd());
        var textDot = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text });
        Assert.Equal(textDot, screen[0, 0].StyleId);
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // 状态段（" · 运行中"）dim：● 2 列 + "fork: " 6 列 + "检查README" 10 列 → col 18 起
        Assert.Equal(dim, screen[0, 18].StyleId);
    }

    [Fact]
    public void Render_CompletedTask_DotUsesSuccessColor()
    {
        using var screen = new Screen(cols: 40, rows: 1);
        var panel = new BackgroundTaskPanel();

        panel.Upsert("teammate", "tm-1", "审阅PR", "completed");
        panel.Render(screen, new Rect(0, 0, 40, 1), Theme.Dark);

        Assert.Equal("● teammate: 审阅PR · 已完成", RowText.Of(screen, 0, 0, 40).TrimEnd());
        var success = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Success });
        Assert.Equal(success, screen[0, 0].StyleId);
    }

    [Fact]
    public void Render_FailedAndCancelledTasks_DotUsesErrorColor()
    {
        using var screen = new Screen(cols: 40, rows: 2);
        var panel = new BackgroundTaskPanel();

        panel.Upsert("fork", "f-1", "构建", "failed");
        panel.Upsert("fork", "f-2", "测试", "cancelled");
        panel.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        Assert.Equal("● fork: 构建 · 失败", RowText.Of(screen, 0, 0, 40).TrimEnd());
        Assert.Equal("● fork: 测试 · 已取消", RowText.Of(screen, 1, 0, 40).TrimEnd());
        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal(error, screen[0, 0].StyleId);
        Assert.Equal(error, screen[1, 0].StyleId);
    }

    [Fact]
    public void Render_UnknownState_ShowsRawStateWithDefaultColor()
    {
        using var screen = new Screen(cols: 40, rows: 1);
        var panel = new BackgroundTaskPanel();

        panel.Upsert("fork", "f-9", "探测", "paused");
        panel.Render(screen, new Rect(0, 0, 40, 1), Theme.Dark);

        // 协议演进宽容 — 未知状态原样显示、色回退 running 形态（theme.Text）
        Assert.Equal("● fork: 探测 · paused", RowText.Of(screen, 0, 0, 40).TrimEnd());
        var textDot = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text });
        Assert.Equal(textDot, screen[0, 0].StyleId);
    }

    [Fact]
    public void Render_OverLimit_ShowsWindowWithMoreHintRow()
    {
        using var screen = new Screen(cols: 40, rows: 11);
        var panel = new BackgroundTaskPanel();
        for (var i = 1; i <= 11; i++)
        {
            panel.Upsert("fork", $"fork-{i}", $"任务{i}", "running");
        }

        panel.Render(screen, new Rect(0, 0, 40, 11), Theme.Dark);

        // 前 10 条逐行渲染（插入序窗口），第 11 条不可见，末行 "+1 more" dim
        Assert.Equal("● fork: 任务1 · 运行中", RowText.Of(screen, 0, 0, 40).TrimEnd());
        Assert.Equal("● fork: 任务10 · 运行中", RowText.Of(screen, 9, 0, 40).TrimEnd());
        Assert.Equal("+1 more", RowText.Of(screen, 10, 0, 40).TrimEnd());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[10, 0].StyleId);
    }

    [Fact]
    public void Render_EmptyPanel_ZeroOutput()
    {
        using var screen = new Screen(cols: 20, rows: 1);
        var panel = new BackgroundTaskPanel();

        panel.Render(screen, new Rect(0, 0, 20, 0), Theme.Dark);

        Assert.Equal(Cell.Blank, screen[0, 0]);
    }

    [Fact]
    public void ReplView_BackgroundPanelRendersAboveQueuePanel()
    {
        using var screen = new Screen(cols: 40, rows: 14);
        var view = new ReplView { Footer = new FooterState { Mode = PermissionMode.Auto } };
        view.QueuedInputs = ["排队输入"];
        view.BackgroundTasks.Upsert("fork", "f-1", "检查README", "running");

        view.Render(screen, Theme.Dark);

        // 自底向上：footer 13 / 输入底边线 12 / 文本 11 / 顶边线 10 / 排队面板 9 / 后台任务面板 8
        // （排队面板保持紧贴输入框 — 现有语义零变化；后台任务面板在其上一层）
        Assert.Equal(new string('─', 40), RowText.Of(screen, 10, 0, 40));
        Assert.Equal("❯ 排队输入", RowText.Of(screen, 9, 0, 40).TrimEnd());
        Assert.Equal("● fork: 检查README · 运行中", RowText.Of(screen, 8, 0, 40).TrimEnd());
    }

    [Fact]
    public void ReplView_EmptyBackgroundPanel_LayoutUnchanged()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var view = new ReplView { Footer = new FooterState { Mode = PermissionMode.Auto } };

        view.Render(screen, Theme.Dark);

        // 空面板零渲染 — 布局与无面板完全一致（footer 9 / 底边线 8 / 顶边线 6）
        Assert.Equal(new string('─', 40), RowText.Of(screen, 6, 0, 40));
        Assert.Equal(new string('─', 40), RowText.Of(screen, 8, 0, 40));
        Assert.Equal(Cell.Blank, screen[5, 0]);
    }

    [Fact]
    public void ReplView_MeasureContentHeight_IncludesBackgroundPanelHeight()
    {
        var view = new ReplView { Footer = new FooterState { Mode = PermissionMode.Auto } };
        view.Messages.Add(new UserMessageItem("hello"));
        view.BackgroundTasks.Upsert("fork", "f-1", "检查", "running");

        // 消息 2 + 间隔 1 + 输入区 3 + 后台面板 1 + footer 1 = 8
        Assert.Equal(8, view.MeasureContentHeight(cols: 40));
    }

    /// <summary>选中行 ❯ 前缀（suggestion 色）替换 ● 状态点 + 正文 suggestion 色 + 状态段 dim 保持
    /// （对齐 ModelPicker.RenderModelListRow 焦点形态）；未选中行保持 ● 状态色点 + 默认正文。</summary>
    [Fact]
    public void Render_SelectedEntry_PointerPrefixAndSuggestionColor()
    {
        using var screen = new Screen(cols: 40, rows: 2);
        var panel = new BackgroundTaskPanel();
        panel.Upsert("fork", "f-1", "任务1", "completed");
        panel.Upsert("fork", "f-2", "任务2", "running");
        panel.MoveSelection(1);

        panel.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        // 未选中行（f-1 终态）保持 ● 状态色点前缀
        Assert.Equal("● fork: 任务1 · 已完成", RowText.Of(screen, 0, 0, 40).TrimEnd());
        // 选中行（f-2）❯ 前缀替换 ● 状态点 — 状态信息由行尾状态段保留
        Assert.Equal("❯ fork: 任务2 · 运行中", RowText.Of(screen, 1, 0, 40).TrimEnd());
        var selection = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Suggestion });
        Assert.Equal(selection, screen[1, 0].StyleId);
        // 选中行状态段（" · 运行中"）dim 保持：❯ 1 列 + 空格 1 + "fork: " 6 + "任务2" 5 → col 13 起
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[1, 13].StyleId);
    }

    /// <summary>选中行超出窗口 — 滚动窗口跟随保证选中行可见（对齐 ModelPicker.MoveFocus 窗口跟随先例）。</summary>
    [Fact]
    public void Render_SelectionBeyondWindow_ScrollsWindowToKeepSelectionVisible()
    {
        using var screen = new Screen(cols: 40, rows: 11);
        var panel = new BackgroundTaskPanel();
        for (var i = 1; i <= 12; i++)
        {
            panel.Upsert("fork", $"f-{i}", $"任务{i}", "running");
        }

        for (var i = 0; i < 12; i++)
        {
            panel.MoveSelection(1);
        }

        Assert.Equal(11, panel.SelectedIndex);
        panel.Render(screen, new Rect(0, 0, 40, 11), Theme.Dark);

        // 窗口跟随：选中第 12 条（索引 11）→ WindowStart=2，首行任务3、末可见行任务12（选中 ❯）
        Assert.Equal(2, panel.WindowStart);
        Assert.Equal("● fork: 任务3 · 运行中", RowText.Of(screen, 0, 0, 40).TrimEnd());
        Assert.Equal("❯ fork: 任务12 · 运行中", RowText.Of(screen, 9, 0, 40).TrimEnd());
        Assert.Equal("+2 more", RowText.Of(screen, 10, 0, 40).TrimEnd());
    }

    /// <summary>ClearSelection — 选中归 -1（IsSelectionActive=false）+ 滚动窗口归 0（回默认视图）。</summary>
    [Fact]
    public void ClearSelection_ResetsIndexAndWindow()
    {
        var panel = new BackgroundTaskPanel();
        for (var i = 1; i <= 12; i++)
        {
            panel.Upsert("fork", $"f-{i}", $"任务{i}", "running");
        }

        for (var i = 0; i < 12; i++)
        {
            panel.MoveSelection(1);
        }

        Assert.True(panel.IsSelectionActive);
        Assert.True(panel.WindowStart > 0);

        panel.ClearSelection();

        Assert.False(panel.IsSelectionActive);
        Assert.Equal(-1, panel.SelectedIndex);
        Assert.Equal(0, panel.WindowStart);
    }

    /// <summary>Entry 快照字段形态（详情对话框构造数据源 — M8-8 登记遗留收口）：
    /// Upsert 投影五字段直通保真 + record with 替换式更新（非原地突变 — 面板/对话框快照隔离依据）。</summary>
    [Fact]
    public void BackgroundTaskEntry_SnapshotFields_PreserveUpsertedValues()
    {
        var panel = new BackgroundTaskPanel();
        panel.Upsert("teammate", "tm-7", "审阅架构文档", "running");

        var snapshot = panel.Tasks[0];
        Assert.Equal(new BackgroundTaskEntry("teammate", "tm-7", "审阅架构文档", "running"), snapshot);

        // record with 替换式更新（Upsert 终态翻转实现形态）— 原快照不可变
        var finalized = snapshot with { State = "completed", FinalizedAtMs = 1000 };
        Assert.Equal("completed", finalized.State);
        Assert.Equal(1000, finalized.FinalizedAtMs);
        Assert.Equal("running", snapshot.State);
        Assert.Equal(0, snapshot.FinalizedAtMs);
    }

    /// <summary>StatusText 汉化映射（internal 化 — BackgroundTaskDetailDialog 详情状态行复用同一映射源，
    /// 面板状态段与详情状态行不双维护）：已知四态汉化 + 未知值宽容原样。</summary>
    [Fact]
    public void StatusText_MapsKnownAndUnknownStates_DetailDialogReusesPanelMapping()
    {
        Assert.Equal("运行中", BackgroundTaskPanel.StatusText("running"));
        Assert.Equal("已完成", BackgroundTaskPanel.StatusText("completed"));
        Assert.Equal("失败", BackgroundTaskPanel.StatusText("failed"));
        Assert.Equal("已取消", BackgroundTaskPanel.StatusText("cancelled"));
        // 协议演进宽容 — 未知值原样显示
        Assert.Equal("paused", BackgroundTaskPanel.StatusText("paused"));
    }
}
