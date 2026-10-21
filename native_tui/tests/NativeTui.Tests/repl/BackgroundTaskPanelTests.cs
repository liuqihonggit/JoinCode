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

        // 终态驻留 — 首版不自动驱逐（基准 evictAfter 过期驱逐下批做）
        Assert.Equal(3, panel.Tasks.Count);
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
}
