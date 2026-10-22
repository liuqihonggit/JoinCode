namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK065 F2 命令交互模态测试 — 斜杠命令 confirm/prompt 回调接 TUI 模态对话框：
/// CommandDialogCoordinator（TCS 桥，对齐 PermissionCoordinator 形态：Begin 取代旧等待者/
/// Complete 幂等/无挂起 Wait 返回已完成占位）+ ReplShell 模态按键路由
/// （confirm：数字/Enter/上下/Esc；prompt：字符插入/退格/Enter/Esc）+ 回合结束自动收尾（取消语义）。
/// 同步桥接冒烟（ConfirmCommand 后台线程阻塞等 TUI 结果）对齐 TuiPermissionBridge.Confirm 的
/// 引擎线程挂起先例（ADR 0142 ConfirmAsync 异步等待）。
/// </summary>
public sealed class CommandDialogTests
{
    /// <summary>构造测试 shell（FakeReplEngine + 固定尺寸源）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>按键注入（Enter/Esc/数字/字符/退格/上下箭头共用）。</summary>
    private static void Press(ReplShell shell, ConsoleKey key, char c = '\0') =>
        shell.HandleKey(new ConsoleKeyInfo(key is ConsoleKey.Enter ? '\r' : c, key,
            shift: false, alt: false, control: false));

    // ---- CommandDialogCoordinator ----

    /// <summary>BeginConfirm 挂起 — IsPending + 消息就位 + 等待未完成。</summary>
    [Fact]
    public void BeginConfirm_PendsRequest()
    {
        var coordinator = new CommandDialogCoordinator();

        coordinator.BeginConfirm("确定要退出吗？");

        Assert.True(coordinator.IsPending);
        Assert.False(coordinator.IsPrompt);
        Assert.Equal("确定要退出吗？", coordinator.Message);
        Assert.False(coordinator.WaitConfirm().IsCompleted);
    }

    /// <summary>BeginPrompt 挂起 — IsPrompt 置位 + 输入缓冲清空。</summary>
    [Fact]
    public void BeginPrompt_PendsInputRequest()
    {
        var coordinator = new CommandDialogCoordinator();

        coordinator.BeginPrompt("请输入提交信息:");

        Assert.True(coordinator.IsPending);
        Assert.True(coordinator.IsPrompt);
        Assert.Equal("请输入提交信息:", coordinator.Message);
        Assert.False(coordinator.WaitPrompt().IsCompleted);
    }

    /// <summary>Begin 二次请求 — 旧等待者取消（对齐 PermissionCoordinator 取代语义）。</summary>
    [Fact]
    public async Task BeginConfirm_SecondRequest_CancelsFirstWaiter()
    {
        var coordinator = new CommandDialogCoordinator();
        coordinator.BeginConfirm("第一问");
        var first = coordinator.WaitConfirm();

        coordinator.BeginConfirm("第二问");

        await Assert.ThrowsAsync<TaskCanceledException>(() => first);
        Assert.Equal("第二问", coordinator.Message);
    }

    /// <summary>CompleteConfirm — 等待者拿到结果 + 状态清空。</summary>
    [Fact]
    public async Task CompleteConfirm_ResolvesWaiterAndClears()
    {
        var coordinator = new CommandDialogCoordinator();
        coordinator.BeginConfirm("确认吗");
        var task = coordinator.WaitConfirm();

        coordinator.CompleteConfirm(true);

        Assert.True(await task);
        Assert.False(coordinator.IsPending);
        Assert.Null(coordinator.Message);
    }

    /// <summary>CompletePrompt — 等待者拿到输入 + 状态清空；null 取消语义。</summary>
    [Fact]
    public async Task CompletePrompt_ResolvesWaiterWithInput()
    {
        var coordinator = new CommandDialogCoordinator();
        coordinator.BeginPrompt("输入");
        var task = coordinator.WaitPrompt();

        coordinator.CompletePrompt("fix: 修复");

        Assert.Equal("fix: 修复", await task);
        Assert.False(coordinator.IsPending);
    }

    /// <summary>Complete 幂等 — 二次 Complete 不影响首个结果（对齐先例）。</summary>
    [Fact]
    public async Task Complete_Twice_KeepsFirstResult()
    {
        var coordinator = new CommandDialogCoordinator();
        coordinator.BeginConfirm("确认吗");
        var task = coordinator.WaitConfirm();

        coordinator.CompleteConfirm(false);
        coordinator.CompleteConfirm(true);

        Assert.False(await task);
    }

    /// <summary>无挂起守卫 — Wait 立即返回降级结果（false/null 占位，防调用线程死等）。</summary>
    [Fact]
    public async Task Wait_WithoutBegin_CompletesDefaultImmediately()
    {
        var coordinator = new CommandDialogCoordinator();

        Assert.True(coordinator.WaitConfirm().IsCompleted);
        Assert.False(await coordinator.WaitConfirm());
        Assert.True(coordinator.WaitPrompt().IsCompleted);
        Assert.Null(await coordinator.WaitPrompt());
    }

    /// <summary>无挂起时 Complete — no-op 不抛（对齐先例）。</summary>
    [Fact]
    public void Complete_WithoutBegin_NoOp()
    {
        var coordinator = new CommandDialogCoordinator();

        coordinator.CompleteConfirm(true);
        coordinator.CompletePrompt("x");

        Assert.False(coordinator.IsPending);
    }

    /// <summary>confirm 焦点环绕 — 两档（0/1）上下环绕移动。</summary>
    [Fact]
    public void MoveConfirmFocus_WrapsTwoOptions()
    {
        var coordinator = new CommandDialogCoordinator();
        coordinator.BeginConfirm("确认吗");

        Assert.Equal(0, coordinator.ConfirmFocusIndex);
        coordinator.MoveConfirmFocus(1);
        Assert.Equal(1, coordinator.ConfirmFocusIndex);
        coordinator.MoveConfirmFocus(1);
        Assert.Equal(0, coordinator.ConfirmFocusIndex);
        coordinator.MoveConfirmFocus(-1);
        Assert.Equal(1, coordinator.ConfirmFocusIndex);
    }

    /// <summary>prompt 输入编辑 — 字符追加 + 退格删除尾字符。</summary>
    [Fact]
    public void PromptInput_AppendAndBackspace_EditsBuffer()
    {
        var coordinator = new CommandDialogCoordinator();
        coordinator.BeginPrompt("输入");
        coordinator.AppendPromptInput('o');
        coordinator.AppendPromptInput('k');

        Assert.Equal("ok", coordinator.PromptInput);

        coordinator.BackspacePromptInput();
        Assert.Equal("o", coordinator.PromptInput);
    }

    // ---- ReplShell 模态路由 ----

    /// <summary>confirm 数字直选 — '1' 完成为 true + 对话框项从滚动区移除。</summary>
    [Fact]
    public async Task ConfirmDialog_DigitOne_CompletesTrueAndRemovesItem()
    {
        var (shell, _) = CreateShell();
        shell.BeginCommandConfirm("确定要退出吗？");
        var task = shell.CommandDialog.WaitConfirm();

        Press(shell, ConsoleKey.D1, '1');

        Assert.True(task.IsCompleted);
        Assert.True(await task);
        Assert.False(shell.CommandDialog.IsPending);
        Assert.DoesNotContain(shell.View.Messages.Items, static item => item is CommandDialogItem);
    }

    /// <summary>confirm 数字直选 — '2' 完成为 false。</summary>
    [Fact]
    public async Task ConfirmDialog_DigitTwo_CompletesFalse()
    {
        var (shell, _) = CreateShell();
        shell.BeginCommandConfirm("确认吗");
        var task = shell.CommandDialog.WaitConfirm();

        Press(shell, ConsoleKey.D2, '2');

        Assert.True(task.IsCompleted);
        Assert.False(await task);
    }

    /// <summary>confirm Esc — 取消完成为 false。</summary>
    [Fact]
    public async Task ConfirmDialog_Escape_CompletesFalse()
    {
        var (shell, _) = CreateShell();
        shell.BeginCommandConfirm("确认吗");
        var task = shell.CommandDialog.WaitConfirm();

        Press(shell, ConsoleKey.Escape);

        Assert.True(task.IsCompleted);
        Assert.False(await task);
    }

    /// <summary>confirm Enter — 选焦点档：默认焦点 Yes=true，下移后 No=false。</summary>
    [Fact]
    public async Task ConfirmDialog_Enter_CompletesFocusedOption()
    {
        var (shell, _) = CreateShell();
        shell.BeginCommandConfirm("确认吗");
        var task = shell.CommandDialog.WaitConfirm();

        Press(shell, ConsoleKey.Enter);
        Assert.True(await task);

        shell.BeginCommandConfirm("再确认");
        var second = shell.CommandDialog.WaitConfirm();
        Press(shell, ConsoleKey.DownArrow);
        Press(shell, ConsoleKey.Enter);
        Assert.False(await second);
    }

    /// <summary>prompt 输入 + Enter — 完成携带输入文本。</summary>
    [Fact]
    public async Task PromptDialog_TypingAndEnter_CompletesWithInput()
    {
        var (shell, _) = CreateShell();
        shell.BeginCommandPrompt("请输入提交信息:");
        var task = shell.CommandDialog.WaitPrompt();

        Press(shell, ConsoleKey.O, 'o');
        Press(shell, ConsoleKey.K, 'k');
        Press(shell, ConsoleKey.Enter);

        Assert.True(task.IsCompleted);
        Assert.Equal("ok", await task);
        Assert.DoesNotContain(shell.View.Messages.Items, static item => item is CommandDialogItem);
    }

    /// <summary>prompt 退格 — 删除尾字符后再 Enter。</summary>
    [Fact]
    public async Task PromptDialog_Backspace_EditsInputBeforeSubmit()
    {
        var (shell, _) = CreateShell();
        shell.BeginCommandPrompt("输入");
        var task = shell.CommandDialog.WaitPrompt();

        Press(shell, ConsoleKey.A, 'a');
        Press(shell, ConsoleKey.B, 'b');
        Press(shell, ConsoleKey.Backspace);
        Press(shell, ConsoleKey.Enter);

        Assert.Equal("a", await task);
    }

    /// <summary>prompt Esc — 取消完成为 null。</summary>
    [Fact]
    public async Task PromptDialog_Escape_CompletesNull()
    {
        var (shell, _) = CreateShell();
        shell.BeginCommandPrompt("输入");
        var task = shell.CommandDialog.WaitPrompt();

        Press(shell, ConsoleKey.Escape);

        Assert.True(task.IsCompleted);
        Assert.Null(await task);
    }

    /// <summary>模态吞键 — 挂起期间字符不进主输入框（对齐权限对话框模态语义）。</summary>
    [Fact]
    public void CommandDialog_WhilePending_SwallowsKeysFromMainInput()
    {
        var (shell, _) = CreateShell();
        shell.BeginCommandPrompt("输入");

        Press(shell, ConsoleKey.A, 'a');

        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>回合结束自动收尾 — 挂起中收到 turn_completed（非忙）→ 取消语义收尾（confirm false/prompt null）。</summary>
    [Fact]
    public async Task DrainEngine_TurnEndWhilePending_AutoCancelsDialog()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
        shell.BeginCommandConfirm("确认吗");
        var confirmTask = shell.CommandDialog.WaitConfirm();

        engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
        shell.DrainEngine();

        Assert.True(confirmTask.IsCompleted);
        Assert.False(await confirmTask);
        Assert.False(shell.CommandDialog.IsPending);

        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
        shell.BeginCommandPrompt("输入");
        var promptTask = shell.CommandDialog.WaitPrompt();
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
        shell.DrainEngine();

        Assert.True(promptTask.IsCompleted);
        Assert.Null(await promptTask);
    }

    /// <summary>同步桥接冒烟 — ConfirmCommand 后台线程阻塞等 TUI 结果，用户按键后释放返回 true
    /// （对齐 TuiPermissionBridge.Confirm 引擎线程挂起先例）。</summary>
    [Fact]
    public async Task ConfirmCommand_BackgroundThreadBridge_UserAccepts_ReturnsTrue()
    {
        var (shell, _) = CreateShell();
        var bridge = Task.Run(() => shell.ConfirmCommand("确定要提交吗？"));

        Assert.True(SpinWaitUntil(() => shell.CommandDialog.IsPending), "桥线程应挂起对话框");
        Press(shell, ConsoleKey.D1, '1');

        var winner = await Task.WhenAny(bridge, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(bridge, winner);
        Assert.True(await bridge);
    }

    /// <summary>自旋等待谓词成立（2s 预算 — 单进程内存操作，确定性足够）。</summary>
    private static bool SpinWaitUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            System.Threading.Thread.Sleep(10);
        }

        return false;
    }
}
