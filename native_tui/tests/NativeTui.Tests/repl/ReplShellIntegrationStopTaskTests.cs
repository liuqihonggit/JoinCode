namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell x 停止后台任务集成测试（M8-8 后续 — 空闲态 x 停止面板运行中任务，对齐基准
/// BackgroundTasksDialog.tsx:268-289 x 停止）：空闲空输入 + 面板 running 条目 + 委托已注入 →
/// 委托被调（首个 running 的 (kind,taskId) — 首版无选中导航回退）；无 running / 有输入文本 /
/// 忙时 → x 走正常字符插入（打字优先）；未注入委托 → 降级字符插入；面板无条目时 x 恒插入。
/// </summary>
public sealed class ReplShellIntegrationStopTaskTests
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

    private static void Char(ReplShell shell, char c) =>
        shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, shift: false, alt: false, control: false));

    private static void X(ReplShell shell) => Char(shell, 'x');

    /// <summary>空闲空输入 + 面板 running 条目 — 委托被调（首个 running 目标）+ x 不进输入框。</summary>
    [Fact]
    public void StopTask_XKey_IdleEmptyInput_CallsDelegateWithFirstRunning()
    {
        var (shell, _, stops) = CreateShell();
        shell.View.BackgroundTasks.Upsert("fork", "fork-1", "任务1", "completed");
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");

        X(shell);

        Assert.Equal([("teammate", "tm-1")], stops);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>面板无 running 条目 — x 走正常字符插入（委托零调用）。</summary>
    [Fact]
    public void StopTask_XKey_NoRunningTask_InsertsAsCharacter()
    {
        var (shell, _, stops) = CreateShell();
        shell.View.BackgroundTasks.Upsert("fork", "fork-1", "任务1", "completed");

        X(shell);

        Assert.Empty(stops);
        Assert.Equal("x", shell.Input.Text);
    }

    /// <summary>有输入文本 — x 走正常字符插入（打字优先，即使面板有 running 条目）。</summary>
    [Fact]
    public void StopTask_XKey_WithInputText_InsertsAsCharacter()
    {
        var (shell, _, stops) = CreateShell();
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");
        Char(shell, 'a');

        X(shell);

        Assert.Empty(stops);
        Assert.Equal("ax", shell.Input.Text);
    }

    /// <summary>忙时 — 空闲态门槛拦截（x 不停止，字符进输入框）。</summary>
    [Fact]
    public void StopTask_XKey_WhileBusy_InsertsAsCharacter()
    {
        var (shell, engine, stops) = CreateShell();
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();

        X(shell);

        Assert.Empty(stops);
        Assert.Equal("x", shell.Input.Text);
    }

    /// <summary>未注入停止委托 — 降级字符插入（Core 零依赖委托 null 容错）。</summary>
    [Fact]
    public void StopTask_XKey_WithoutDelegate_InsertsAsCharacter()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");

        X(shell);

        Assert.Equal("x", shell.Input.Text);
    }
}
