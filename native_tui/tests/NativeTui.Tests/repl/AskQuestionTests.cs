namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK103 S14 原生 TUI 问答测试 — AskQuestionCoordinator（TCS 桥：Begin 取代旧等待者/
/// 多题推进/多选切换/部分答案 Defer/无挂起 Wait 取消占位）+ ReplShell 模态按键路由
/// （上下环绕/Enter 提交/Space 多选/数字直选/Esc 取消）+ 后台回调探测关闭 + 回合结束自动取消。
/// </summary>
public sealed class AskQuestionTests
{
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

    private static void Press(ReplShell shell, ConsoleKey key, char c = '\0') =>
        shell.HandleKey(new ConsoleKeyInfo(key is ConsoleKey.Enter ? '\r' : c, key,
            shift: false, alt: false, control: false));

    private static bool SpinWaitUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(5);
        }
        return condition();
    }

    private static TuiAskQuestion Single(params string[] labels) =>
        new("选哪个？", "问题", labels.Select(l => new TuiAskOption(l, string.Empty)).ToList(), MultiSelect: false);

    private static TuiAskQuestion Multi(params string[] labels) =>
        new("选多个？", "多选", labels.Select(l => new TuiAskOption(l, string.Empty)).ToList(), MultiSelect: true);

    // ---- AskQuestionCoordinator ----

    /// <summary>单选 — 焦点移动后 Enter 提交，答案取焦点项。</summary>
    [Fact]
    public async Task Coordinator_Single_MoveFocusThenSubmit()
    {
        var coordinator = new AskQuestionCoordinator();
        coordinator.Begin([Single("A", "B", "C")]);
        var task = coordinator.Wait();

        coordinator.MoveFocus(1);
        Assert.True(coordinator.Submit());

        var result = await task;
        Assert.True(result.Succeeded);
        Assert.Equal("B", result.Answers["选哪个？"]);
        Assert.False(coordinator.IsPending);
    }

    /// <summary>多选 — Space 切换焦点项，未选禁止提交，选中后提交连接标签。</summary>
    [Fact]
    public async Task Coordinator_Multi_ToggleThenSubmitJoinedLabels()
    {
        var coordinator = new AskQuestionCoordinator();
        coordinator.Begin([Multi("A", "B", "C")]);
        var task = coordinator.Wait();

        Assert.False(coordinator.Submit());

        coordinator.ToggleFocus();
        coordinator.MoveFocus(2);
        coordinator.ToggleFocus();
        Assert.True(coordinator.Submit());

        var result = await task;
        Assert.True(result.Succeeded);
        Assert.Equal("A, C", result.Answers["选多个？"]);
    }

    /// <summary>多题 — 第一题提交后停留第二题，逐题累积答案；数字直选单选立即推进。</summary>
    [Fact]
    public async Task Coordinator_MultipleQuestions_AdvancesAndAccumulates()
    {
        var coordinator = new AskQuestionCoordinator();
        coordinator.Begin([Single("A", "B"), Multi("X", "Y")]);
        var task = coordinator.Wait();

        Assert.True(coordinator.Submit());
        Assert.True(coordinator.IsPending);
        Assert.Equal(1, coordinator.QuestionIndex);

        Assert.False(coordinator.SelectNumber(2));
        Assert.True(coordinator.Submit());

        var result = await task;
        Assert.True(result.Succeeded);
        Assert.Equal("A", result.Answers["选哪个？"]);
        Assert.Equal("Y", result.Answers["选多个？"]);
    }

    /// <summary>数字越界/重复点击 — 不推进不改变焦点答案。</summary>
    [Fact]
    public void Coordinator_SelectNumberOutOfRange_NoOp()
    {
        var coordinator = new AskQuestionCoordinator();
        coordinator.Begin([Single("A", "B")]);

        Assert.False(coordinator.SelectNumber(0));
        Assert.False(coordinator.SelectNumber(3));
        Assert.True(coordinator.IsPending);
    }

    /// <summary>Esc 取消 — Cancelled 且无答案。</summary>
    [Fact]
    public async Task Coordinator_Cancel_ReturnsCancelled()
    {
        var coordinator = new AskQuestionCoordinator();
        coordinator.Begin([Single("A", "B")]);
        var task = coordinator.Wait();

        coordinator.Cancel();

        var result = await task;
        Assert.True(result.Cancelled);
        Assert.Empty(result.Answers);
    }

    /// <summary>后台回调 Defer — 携带已答部分答案。</summary>
    [Fact]
    public async Task Coordinator_Defer_CarriesPartialAnswers()
    {
        var coordinator = new AskQuestionCoordinator();
        coordinator.Begin([Single("A", "B"), Single("C", "D")]);
        var task = coordinator.Wait();

        coordinator.Submit();
        coordinator.Defer();

        var result = await task;
        Assert.True(result.Deferred);
        Assert.Equal("A", result.Answers["选哪个？"]);
        Assert.DoesNotContain("C", result.Answers.Keys);
    }

    /// <summary>Begin 取代旧等待者 — 旧任务取消，新问题集从第一题开始。</summary>
    [Fact]
    public async Task Coordinator_BeginReplacesPending()
    {
        var coordinator = new AskQuestionCoordinator();
        coordinator.Begin([Single("A", "B")]);
        var first = coordinator.Wait();

        coordinator.Begin([Single("C", "D")]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.True(coordinator.IsPending);
        Assert.Equal("C", coordinator.Current?.Options[0].Label);
    }

    /// <summary>无挂起 Wait — 已完成取消占位（不抛不挂）。</summary>
    [Fact]
    public async Task Coordinator_WaitWithoutPending_ReturnsCancelled()
    {
        var coordinator = new AskQuestionCoordinator();

        var result = await coordinator.Wait();
        Assert.True(result.Cancelled);
    }

    // ---- ReplShell 模态路由 ----

    /// <summary>方向键 + Enter — 对话框模态消费按键，提交后移除对话框项。</summary>
    [Fact]
    public async Task Shell_ArrowAndEnter_SubmitFocusedOption()
    {
        var (shell, _) = CreateShell();
        shell.BeginAskQuestions([Single("A", "B")]);
        var task = shell.AskQuestions.Wait();

        Press(shell, ConsoleKey.DownArrow);
        Press(shell, ConsoleKey.Enter);

        var result = await task;
        Assert.True(result.Succeeded);
        Assert.Equal("B", result.Answers["选哪个？"]);
        Assert.False(shell.AskQuestions.IsPending);
    }

    /// <summary>数字直选 — 单选立即提交。</summary>
    [Fact]
    public async Task Shell_Digit_SelectsAndSubmits()
    {
        var (shell, _) = CreateShell();
        shell.BeginAskQuestions([Single("A", "B", "C")]);
        var task = shell.AskQuestions.Wait();

        Press(shell, ConsoleKey.D3, '3');

        var result = await task;
        Assert.True(result.Succeeded);
        Assert.Equal("C", result.Answers["选哪个？"]);
    }

    /// <summary>Space 多选切换 + Enter 提交。</summary>
    [Fact]
    public async Task Shell_SpaceTogglesInMultiSelect()
    {
        var (shell, _) = CreateShell();
        shell.BeginAskQuestions([Multi("A", "B")]);
        var task = shell.AskQuestions.Wait();

        Press(shell, ConsoleKey.Spacebar, ' ');
        Press(shell, ConsoleKey.Enter);

        var result = await task;
        Assert.True(result.Succeeded);
        Assert.Equal("A", result.Answers["选多个？"]);
    }

    /// <summary>Esc 取消 — 对话框关闭返回 Cancelled。</summary>
    [Fact]
    public async Task Shell_Escape_Cancels()
    {
        var (shell, _) = CreateShell();
        shell.BeginAskQuestions([Single("A", "B")]);
        var task = shell.AskQuestions.Wait();

        Press(shell, ConsoleKey.Escape);

        Assert.True((await task).Cancelled);
        Assert.False(shell.AskQuestions.IsPending);
    }

    /// <summary>后台回调探测 — 帧拍探测为 true 时自动关闭问答并携带已答部分返回 Deferred。</summary>
    [Fact]
    public async Task Shell_BackgroundProbe_DefersWithPartialAnswers()
    {
        var (shell, _) = CreateShell();
        shell.BeginAskQuestions([Single("A", "B"), Single("C", "D")]);
        var task = shell.AskQuestions.Wait();
        Press(shell, ConsoleKey.Enter);

        shell.AskQuestionInterruptProbe = static () => true;
        shell.CheckAskQuestionInterrupt();

        var result = await task;
        Assert.True(result.Deferred);
        Assert.Equal("A", result.Answers["选哪个？"]);
        Assert.False(shell.AskQuestions.IsPending);
    }

    /// <summary>引擎回合结束 — 挂起问答自动取消（宿主同步桥解除阻塞，不假定答案）。</summary>
    [Fact]
    public async Task Shell_EngineIdle_AutoCancels()
    {
        var (shell, _) = CreateShell();
        shell.BeginAskQuestions([Single("A", "B")]);
        var task = shell.AskQuestions.Wait();

        shell.DrainEngine();

        Assert.True((await task).Cancelled);
        Assert.False(shell.AskQuestions.IsPending);
    }

    /// <summary>同步桥 — 后台线程 AskUserQuestions 阻塞，主线程按键释放返回结果。</summary>
    [Fact]
    public async Task Shell_BackgroundBridge_KeyReleases()
    {
        var (shell, _) = CreateShell();
        var bridge = Task.Run(() => shell.AskUserQuestions([Single("A", "B")]));

        Assert.True(SpinWaitUntil(() => shell.AskQuestions.IsPending), "桥线程应挂起问答对话框");
        Press(shell, ConsoleKey.Enter);

        var winner = await Task.WhenAny(bridge, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(bridge, winner);
        var result = await bridge;
        Assert.True(result.Succeeded);
        Assert.Equal("A", result.Answers["选哪个？"]);
    }
}
