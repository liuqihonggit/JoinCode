namespace NativeTui.Tests.Repl;

/// <summary>
/// 权限对话框测试（M7-3）— 对齐基准 FallbackPermissionRequest 形态：
/// PermissionCoordinator TCS 桥（Begin/Complete/Wait + 焦点环绕）+ PermissionRequestItem 渲染
/// （仅顶边框 permission 色 round 无角全宽 / bold permission 标题 paddingX=1 / 工具行 3 空格缩进 bold 名 /
/// 问题描述 dim / "Do you want to proceed?" / Select 三档数字前缀选项[1.Yes 2.始终允许 3.No] /
/// 焦点行 ❯ suggestion + index dim / Esc to cancel dim）+ ReplShell 模态改道
/// （挂起时数字/Esc/上下/Enter 路由对话框，其余吞掉；回合结束自动 Deny 收尾）。
/// </summary>
public sealed class PermissionDialogTests
{
    /// <summary>构造挂起请求的 coordinator（默认 Bash/run tests）。</summary>
    private static PermissionCoordinator BeginCoordinator(string tool = "Bash", string description = "run tests")
    {
        var coordinator = new PermissionCoordinator();
        coordinator.Begin(new PermissionRequest(tool, description));
        return coordinator;
    }

    // ---- PermissionCoordinator ----

    /// <summary>Begin 挂起请求 — IsPending + Request 就位。</summary>
    [Fact]
    public void Begin_SetsPendingRequest()
    {
        var coordinator = BeginCoordinator("Edit", "write a.cs");

        Assert.True(coordinator.IsPending);
        Assert.Equal("Edit", coordinator.Request?.ToolName);
        Assert.Equal("write a.cs", coordinator.Request?.Description);
    }

    /// <summary>Begin 二次请求 — 旧等待者取消（对齐青蛙TUI DialogRequest 取代语义）。</summary>
    [Fact]
    public async Task Begin_SecondRequest_CancelsFirstWaiter()
    {
        var coordinator = new PermissionCoordinator();
        coordinator.Begin(new PermissionRequest("A", "a"));
        var first = coordinator.Wait();

        coordinator.Begin(new PermissionRequest("B", "b"));

        await Assert.ThrowsAsync<TaskCanceledException>(() => first);
        Assert.Equal("B", coordinator.Request?.ToolName);
    }

    /// <summary>Complete — 等待者拿到选择 + 状态清空。</summary>
    [Fact]
    public async Task Complete_ResolvesWaiterAndClears()
    {
        var coordinator = BeginCoordinator();
        var task = coordinator.Wait();

        coordinator.Complete(PermissionChoice.Allow);

        Assert.Equal(PermissionChoice.Allow, await task);
        Assert.False(coordinator.IsPending);
        Assert.Null(coordinator.Request);
    }

    /// <summary>无挂起时 Complete — no-op 不抛。</summary>
    [Fact]
    public void Complete_WithoutBegin_NoOp()
    {
        var coordinator = new PermissionCoordinator();

        coordinator.Complete(PermissionChoice.Deny);

        Assert.False(coordinator.IsPending);
    }

    /// <summary>Complete 幂等 — 二次 Complete 不影响首个结果。</summary>
    [Fact]
    public async Task Complete_Twice_KeepsFirstResult()
    {
        var coordinator = BeginCoordinator();
        var task = coordinator.Wait();

        coordinator.Complete(PermissionChoice.Deny);
        coordinator.Complete(PermissionChoice.Allow);

        Assert.Equal(PermissionChoice.Deny, await task);
    }

    /// <summary>焦点移动 — 上下环绕（0 上→2，2 下→0），Begin 重置焦点。</summary>
    [Fact]
    public void MoveFocus_WrapsAround()
    {
        var coordinator = BeginCoordinator();

        Assert.Equal(0, coordinator.FocusIndex);
        coordinator.MoveFocus(-1);
        Assert.Equal(2, coordinator.FocusIndex);
        coordinator.MoveFocus(1);
        Assert.Equal(0, coordinator.FocusIndex);

        coordinator.Begin(new PermissionRequest("X", "x"));
        Assert.Equal(0, coordinator.FocusIndex);
    }

    // ---- PermissionRequestItem 渲染 ----

    /// <summary>顶边框 — 全宽 ─ 行 permission 色（round 无角、无左右底边）。</summary>
    [Fact]
    public void Render_TopBorderPermissionColoredFullWidth()
    {
        using var screen = new Screen(cols: 40, rows: 13);
        var coordinator = BeginCoordinator();
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 40, 13), Theme.Dark);

        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Permission });
        Assert.Equal(new Rune('─'), screen[1, 0].Value);
        Assert.Equal(border, screen[1, 0].StyleId);
        Assert.Equal(new Rune('─'), screen[1, 39].Value);
        Assert.Equal(border, screen[1, 39].StyleId);
        Assert.Equal(new string('─', 40), RowText.Of(screen, 1, 0, 40));

        // 仅顶边框：row2 首列是标题 padding 空格（无角字符/竖线）
        Assert.Equal(new Rune(' '), screen[2, 0].Value);
    }

    /// <summary>标题行 — paddingX=1 + bold permission 色 "Tool use"。</summary>
    [Fact]
    public void Render_TitleBoldPermissionPaddedOne()
    {
        using var screen = new Screen(cols: 40, rows: 13);
        var coordinator = BeginCoordinator();
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 40, 13), Theme.Dark);

        var title = screen.Styles.GetOrAdd(new TextStyle { Bold = true, Foreground = Theme.Dark.Permission });
        Assert.Equal(new Rune(' '), screen[2, 0].Value);
        Assert.Equal(new Rune('T'), screen[2, 1].Value);
        Assert.Equal(title, screen[2, 1].StyleId);
        Assert.Equal(title, screen[2, 8].StyleId);
        Assert.Equal(" Tool use", RowText.Of(screen, 2, 0, 9));
    }

    /// <summary>工具行 — 3 空格缩进 + bold 工具名 + 默认样式 (描述参数)；描述行 dim。</summary>
    [Fact]
    public void Render_ToolRowBoldNameDescriptionDim()
    {
        using var screen = new Screen(cols: 40, rows: 13);
        var coordinator = BeginCoordinator("Bash", "run tests");
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 40, 13), Theme.Dark);

        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // row4 = "   Bash(run tests)"：3 空格 + bold Bash + 默认 (run tests)
        Assert.Equal(new Rune('B'), screen[4, 3].Value);
        Assert.Equal(bold, screen[4, 3].StyleId);
        Assert.Equal(new Rune('('), screen[4, 7].Value);
        Assert.Equal(0, screen[4, 7].StyleId);
        Assert.Equal("   Bash(run tests)", RowText.Of(screen, 4, 0, 18));

        // row5 = "   run tests" dim
        Assert.Equal(new Rune('r'), screen[5, 3].Value);
        Assert.Equal(dim, screen[5, 3].StyleId);
        Assert.Equal("   run tests", RowText.Of(screen, 5, 0, 12));
    }

    /// <summary>焦点选项行 — ❯ suggestion + gap + "1. " dim + label suggestion。</summary>
    [Fact]
    public void Render_FocusedOptionPointerSuggestion()
    {
        using var screen = new Screen(cols: 40, rows: 13);
        var coordinator = BeginCoordinator();
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 40, 13), Theme.Dark);

        var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Suggestion });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(new Rune('❯'), screen[8, 0].Value);
        Assert.Equal(suggestion, screen[8, 0].StyleId);
        Assert.Equal(new Rune(' '), screen[8, 1].Value);
        Assert.Equal(new Rune('1'), screen[8, 2].Value);
        Assert.Equal(dim, screen[8, 2].StyleId);
        Assert.Equal(new Rune('Y'), screen[8, 5].Value);
        Assert.Equal(suggestion, screen[8, 5].StyleId);
        Assert.Equal("❯ 1. Yes", RowText.Of(screen, 8, 0, 8));
    }

    /// <summary>非焦点选项行 — 空格 indicator + "2. " dim + label 默认样式。</summary>
    [Fact]
    public void Render_UnfocusedOptionDimIndexDefaultLabel()
    {
        using var screen = new Screen(cols: 48, rows: 13);
        var coordinator = BeginCoordinator();
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 48, 13), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(new Rune(' '), screen[9, 0].Value);
        Assert.Equal(new Rune(' '), screen[9, 1].Value);
        Assert.Equal(new Rune('2'), screen[9, 2].Value);
        Assert.Equal(dim, screen[9, 2].StyleId);
        Assert.Equal(0, screen[9, 5].StyleId);
        Assert.Equal("  2. Yes, and don't ask again for this tool", RowText.Of(screen, 9, 0, 43));

        Assert.Equal("  3. No", RowText.Of(screen, 10, 0, 7));
    }

    /// <summary>问题行 + Esc 提示行 — question 默认样式 / "Esc to cancel" dim（各 1 空格缩进）。</summary>
    [Fact]
    public void Render_QuestionAndEscHint()
    {
        using var screen = new Screen(cols: 40, rows: 13);
        var coordinator = BeginCoordinator();
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 40, 13), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 7, 0, 24));
        Assert.Equal(0, screen[7, 1].StyleId);

        Assert.Equal(" Esc to cancel", RowText.Of(screen, 12, 0, 14));
        Assert.Equal(dim, screen[12, 1].StyleId);
    }

    /// <summary>MeasureHeight — margin1 + border1 + title1 + padY1 + tool1 + desc1 + padY1 + question1 + 3 选项 + escMargin1 + esc1 = 13。</summary>
    [Fact]
    public void MeasureHeight_ThirteenRowsWithDescription()
    {
        var coordinator = BeginCoordinator();
        Assert.Equal(13, new PermissionRequestItem(coordinator).MeasureHeight(width: 40));
    }

    /// <summary>MeasureHeight — 无描述时 12（描述行折叠）。</summary>
    [Fact]
    public void MeasureHeight_TwelveRowsWithoutDescription()
    {
        var coordinator = BeginCoordinator("Bash", "");
        Assert.Equal(12, new PermissionRequestItem(coordinator).MeasureHeight(width: 40));
    }

    // ---- ReplShell 模态改道 ----

    /// <summary>构造测试 shell（复用 ReplShellTests 模式）。</summary>
    private static (ReplShell Shell, FakePermissionEngine Engine) CreateShell()
    {
        var engine = new FakePermissionEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter());
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>模拟按键（ConsoleKeyInfo → HandleKey 全链归一）。</summary>
    private static void Press(ReplShell shell, char c) =>
        shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.D1 + (c - '1'), false, false, false));

    /// <summary>数字 1 — 完成 Allow + 对话框项移除 + 输入缓冲不受污染。</summary>
    [Fact]
    public async Task Shell_Digit1_CompletesAllowAndRemovesItem()
    {
        var (shell, _) = CreateShell();
        shell.BeginPermissionRequest("Bash", "run tests");
        var task = shell.Permission.Wait();
        Assert.Contains(shell.View.Messages.Items, m => m is PermissionRequestItem);

        shell.HandleKey(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));

        Assert.Equal(PermissionChoice.Allow, await task);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is PermissionRequestItem);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>数字 2 — 完成 AlwaysAllow；数字 3 / Esc — 完成 Deny。</summary>
    [Fact]
    public async Task Shell_Digit2And3_MapAlwaysAllowAndDeny()
    {
        var (shell, _) = CreateShell();
        shell.BeginPermissionRequest("Bash", "a");
        var always = shell.Permission.Wait();
        shell.HandleKey(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));
        Assert.Equal(PermissionChoice.AlwaysAllow, await always);

        shell.BeginPermissionRequest("Bash", "b");
        var deny = shell.Permission.Wait();
        shell.HandleKey(new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false));
        Assert.Equal(PermissionChoice.Deny, await deny);

        shell.BeginPermissionRequest("Bash", "c");
        var esc = shell.Permission.Wait();
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        Assert.Equal(PermissionChoice.Deny, await esc);
    }

    /// <summary>Esc 挂起时 — 不触发打断/清输入/退出（对话框模态优先）。</summary>
    [Fact]
    public async Task Shell_EscapeWhilePending_DoesNotInterruptOrExit()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
        shell.BeginPermissionRequest("Bash", "x");
        var task = shell.Permission.Wait();

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.Equal(PermissionChoice.Deny, await task);
        Assert.Equal(0, engine.Interrupts);
        Assert.True(shell.Running);
    }

    /// <summary>上下箭头 — 焦点环绕移动，不进输入缓冲。</summary>
    [Fact]
    public void Shell_ArrowsWhilePending_MoveFocusNotInput()
    {
        var (shell, _) = CreateShell();
        shell.BeginPermissionRequest("Bash", "x");

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal(2, shell.Permission.FocusIndex);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.Equal(0, shell.Permission.FocusIndex);

        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>Enter — 选择焦点项（默认焦点 0 = Allow）。</summary>
    [Fact]
    public async Task Shell_EnterSelectsFocusedOption()
    {
        var (shell, _) = CreateShell();
        shell.BeginPermissionRequest("Bash", "x");
        var task = shell.Permission.Wait();

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal(PermissionChoice.Deny, await task);
    }

    /// <summary>其他字符 — 模态吞掉（不进输入缓冲、不提交）。</summary>
    [Fact]
    public void Shell_OtherCharsSwallowedWhilePending()
    {
        var (shell, engine) = CreateShell();
        shell.BeginPermissionRequest("Bash", "x");

        shell.HandleKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Empty(engine.Submits);
        Assert.True(shell.Permission.IsPending);
    }

    /// <summary>回合结束（完成/打断）— 自动 Deny 收尾 + 对话框移除。</summary>
    [Fact]
    public async Task Shell_TurnEnd_AutoCompletesPendingAsDeny()
    {
        var (shell, engine) = CreateShell();
        shell.BeginPermissionRequest("Bash", "x");
        var task = shell.Permission.Wait();

        engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
        shell.DrainEngine();

        Assert.Equal(PermissionChoice.Deny, await task);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is PermissionRequestItem);
    }

    /// <summary>记录型假引擎（ReplShellTests.FakeEngine 同构）。</summary>
    private sealed class FakePermissionEngine : IReplEngine
    {
        /// <summary>收到的提交文本。</summary>
        public List<string> Submits { get; } = [];

        /// <summary>收到的打断次数。</summary>
        public int Interrupts;

        /// <summary>待排空事件队列。</summary>
        public Queue<BridgeEvent> Events { get; } = new();

        /// <summary>记录提交。</summary>
        public void Submit(string text) => Submits.Add(text);

        /// <summary>记录打断。</summary>
        public void Interrupt() => Interrupts++;

        /// <summary>记录权限模式。</summary>
        public void SetPermissionMode(string mode)
        {
        }

        /// <summary>排空事件队列。</summary>
        public IReadOnlyList<BridgeEvent> DrainEvents()
        {
            var drained = Events.ToList();
            Events.Clear();
            return drained;
        }

        /// <summary>存活标记。</summary>
        public bool IsAlive => true;
    }
}
