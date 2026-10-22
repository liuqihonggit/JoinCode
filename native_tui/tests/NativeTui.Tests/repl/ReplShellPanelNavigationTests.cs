namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 后台任务面板选中导航集成测试（M8-8 登记遗留 — 基准 useBackgroundTaskNavigation.ts:181-189 +
/// BackgroundTasksDialog.tsx 导航态）：Shift+Down 进入选中态（空闲空输入 + 有 running 门槛 — 无 running /
/// 忙时不进入）、选中态 ↑↓ 移动（跳终态 + 窗口跟随）、Enter 查看选中任务详情（ToolResultItem 四元组消息）、
/// x 停止选中任务（注入停止委托捕获）、Esc 退出选中态、模态吞键（字符不进输入框）。
/// 构造形态对齐 ReplShellIntegrationStopTaskTests（面板条目由测试直接 Upsert 种入）。
/// </summary>
public sealed class ReplShellPanelNavigationTests
{
    /// <summary>构造测试 shell（注入记录型停止委托 — 面板条目由测试直接 Upsert 种入）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine, List<(string Kind, string TaskId)> Stops) CreateShell()
    {
        var engine = new FakeReplEngine();
        var stops = new List<(string Kind, string TaskId)>();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            onStopTask: (kind, taskId) => stops.Add((kind, taskId)));
        shell.SeedContent();
        return (shell, engine, stops);
    }

    /// <summary>功能键注入（箭头/Enter/Esc — keyChar 形态对齐 MessageCursorTests 先例）。</summary>
    private static void Press(ReplShell shell, ConsoleKey key, bool shift = false) =>
        shell.HandleKey(new ConsoleKeyInfo(key is ConsoleKey.Enter ? '\r' : '\0', key, shift, alt: false, control: false));

    /// <summary>字符键注入（'a'/'x' 等 — 形态对齐 ReplShellIntegrationStopTaskTests.Char）。</summary>
    private static void Char(ReplShell shell, char c) =>
        shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, shift: false, alt: false, control: false));

    /// <summary>空闲空输入 + 面板有 running 条目 — Shift+Down 进入选中态（首个 running 条目，跳过终态）。</summary>
    [Fact]
    public void Enter_IdleWithRunningTasks_ShiftDownSelectsFirstRunning()
    {
        var (shell, _, _) = CreateShell();
        shell.View.BackgroundTasks.Upsert("fork", "f-1", "任务1", "completed");
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");
        shell.View.BackgroundTasks.Upsert("fork", "f-2", "任务2", "running");

        Press(shell, ConsoleKey.DownArrow, shift: true);

        Assert.True(shell.View.BackgroundTasks.IsSelectionActive);
        // 跳过终态 f-1 → 选中首个 running tm-1
        Assert.Equal(1, shell.View.BackgroundTasks.SelectedIndex);
        Assert.Equal(("teammate", "tm-1"), shell.View.BackgroundTasks.SelectedEntry);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>面板无 running 条目 — Shift+Down 不进入选中态（落入历史导航 — 无历史 no-op）。</summary>
    [Fact]
    public void Enter_NoRunningTasks_ShiftDownDoesNotEnterSelection()
    {
        var (shell, _, _) = CreateShell();
        shell.View.BackgroundTasks.Upsert("fork", "f-1", "任务1", "completed");

        Press(shell, ConsoleKey.DownArrow, shift: true);

        Assert.False(shell.View.BackgroundTasks.IsSelectionActive);
        Assert.Equal(-1, shell.View.BackgroundTasks.SelectedIndex);
    }

    /// <summary>忙时 — 空闲态门槛拦截（回合中面板仍渲染但不可交互，Shift+Down 不进入选中态）。</summary>
    [Fact]
    public void Enter_WhileBusy_ShiftDownDoesNotEnterSelection()
    {
        var (shell, engine, _) = CreateShell();
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();

        Press(shell, ConsoleKey.DownArrow, shift: true);

        Assert.False(shell.View.BackgroundTasks.IsSelectionActive);
    }

    /// <summary>选中态 ↑/↓ 移动选中（MoveSelection 跳终态 + 到头不动不环绕）。</summary>
    [Fact]
    public void Selection_ArrowKeys_MoveSelection()
    {
        var (shell, _, _) = CreateShell();
        shell.View.BackgroundTasks.Upsert("fork", "f-1", "任务1", "running");
        shell.View.BackgroundTasks.Upsert("fork", "f-2", "任务2", "completed");
        shell.View.BackgroundTasks.Upsert("fork", "f-3", "任务3", "running");
        Press(shell, ConsoleKey.DownArrow, shift: true);
        Assert.Equal(0, shell.View.BackgroundTasks.SelectedIndex);

        Press(shell, ConsoleKey.DownArrow);
        // f-2 终态跳过 → f-3
        Assert.Equal(2, shell.View.BackgroundTasks.SelectedIndex);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal(0, shell.View.BackgroundTasks.SelectedIndex);
        Press(shell, ConsoleKey.UpArrow);
        // 到头不动（不环绕）
        Assert.Equal(0, shell.View.BackgroundTasks.SelectedIndex);
    }

    /// <summary>选中态 Enter — 查看选中任务详情（ToolResultItem 四元组消息上屏 + 保持选中态）。</summary>
    [Fact]
    public void Selection_EnterKey_ShowsTaskDetailMessage()
    {
        var (shell, _, _) = CreateShell();
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅PR", "running");
        Press(shell, ConsoleKey.DownArrow, shift: true);

        Press(shell, ConsoleKey.Enter);

        var detail = shell.View.Messages.Items.OfType<ToolResultItem>().Single();
        Assert.Contains("kind: teammate", detail.Text);
        Assert.Contains("id: tm-1", detail.Text);
        Assert.Contains("描述: 审阅PR", detail.Text);
        Assert.Contains("状态: running", detail.Text);
        // 查看后保持选中态（连续查看/停止支持）
        Assert.True(shell.View.BackgroundTasks.IsSelectionActive);
    }

    /// <summary>选中态 x — 停止选中任务（注入停止委托捕获 (kind,taskId) + 字符不进输入框）。</summary>
    [Fact]
    public void Selection_XKey_StopsSelectedTask()
    {
        var (shell, _, stops) = CreateShell();
        shell.View.BackgroundTasks.Upsert("fork", "f-1", "任务1", "completed");
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");
        Press(shell, ConsoleKey.DownArrow, shift: true);
        Assert.Equal(("teammate", "tm-1"), shell.View.BackgroundTasks.SelectedEntry);

        Char(shell, 'x');

        Assert.Equal([("teammate", "tm-1")], stops);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>选中态 Esc — 退出选中态（SelectedIndex 归 -1）。</summary>
    [Fact]
    public void Selection_EscapeKey_ExitsSelection()
    {
        var (shell, _, _) = CreateShell();
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");
        Press(shell, ConsoleKey.DownArrow, shift: true);
        Assert.True(shell.View.BackgroundTasks.IsSelectionActive);

        Press(shell, ConsoleKey.Escape);

        Assert.False(shell.View.BackgroundTasks.IsSelectionActive);
        Assert.Equal(-1, shell.View.BackgroundTasks.SelectedIndex);
    }

    /// <summary>选中态模态吞键 — 字符不进输入框（基准全屏导航态 PromptInput 卸载语义）。</summary>
    [Fact]
    public void Selection_SwallowsCharacterKeys()
    {
        var (shell, _, _) = CreateShell();
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");
        Press(shell, ConsoleKey.DownArrow, shift: true);

        Char(shell, 'a');
        Char(shell, 'b');

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.True(shell.View.BackgroundTasks.IsSelectionActive);
    }
}
