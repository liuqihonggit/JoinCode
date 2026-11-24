namespace NativeTui.Tests.Repl;

/// <summary>
/// Ctrl+B 主会话后台化（ADR 0165 阶段 1）— 「万物皆后台」的前台侧：
/// 查询从提交起有身份，Ctrl+B 只改「前台订阅哪个查询」，不 abort、不重跑、不重复计费。
/// 覆盖：提交接管前台 → Ctrl+B 转后台（不打断）→ 后台事件只入缓冲不渲染 → 再按切回补投；
/// 以及闲且无后台任务时 ctrl+b 仍是编辑域光标左移（Task/Chat 上下文分层）。
/// </summary>
public sealed class QueryBackgroundTests
{
    /// <summary>固定尺寸源（摆脱测试进程 Console 环境依赖 — 对齐 TerminalSizeSource 注入先例）。</summary>
    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Cols, int Rows) Current => (cols, rows);
    }

    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"),
            new StringWriter(), sizeSource: new FixedSizeSource(80, 30));
        shell.SeedContent();
        return (shell, engine);
    }

    private static void Type(ReplShell shell, string text)
    {
        foreach (var ch in text)
        {
            shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, ch.ToString()));
        }
    }

    private static void PressCtrlB(ReplShell shell) =>
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "b", Ctrl: true));

    /// <summary>提交 → 引擎回 TurnStarted（带 queryId）→ 前台进入忙态（事件没被当后台吞掉）。</summary>
    [Fact]
    public void SubmitThenTurnStarted_ForegroundBecomesBusy()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "run");
        shell.Submit();

        Assert.Equal(["run"], engine.Submits);

        // FakeReplEngine 恒返回 q-test — 回放事件必须带同一个 queryId 才算「归因到前台这条查询」
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        shell.DrainEngine();

        Assert.True(shell.IsBusy);
    }

    /// <summary>Ctrl+B：前台转后台 —— 不调用 Interrupt（不打断）、前台回到空闲可继续输入，并给出可执行提示。</summary>
    [Fact]
    public void CtrlB_WhileBusy_BackgroundsWithoutInterrupt()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "long task");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        shell.DrainEngine();
        Assert.True(shell.IsBusy);

        PressCtrlB(shell);

        Assert.Equal(0, engine.Interrupts);
        Assert.False(shell.IsBusy);
        Assert.Contains(shell.View.Messages.Items.OfType<SystemNoticeItem>(),
            item => item.Text.Contains("已转入后台", StringComparison.Ordinal));
    }

    /// <summary>转后台后：该查询的 token/正文只进缓冲，不渲染到前台；再按 Ctrl+B 切回时按序补投。</summary>
    [Fact]
    public void BackgroundQueryEvents_BufferedThenReplayedOnFocus()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "long task");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        shell.DrainEngine();
        PressCtrlB(shell);

        // 后台继续产出 — 前台不得看到（真实引擎逐条事件都带 queryId，见 BridgeEventConsumer.WriteEvent）
        engine.Events.Enqueue(new BridgeEvent.TokenDelta(5, "后台输出") { QueryId = "q-test" });
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("后台输出") { QueryId = "q-test" });
        shell.DrainEngine();

        Assert.DoesNotContain(shell.View.Messages.Items.OfType<AssistantMessageItem>(),
            item => item.Text.Contains("后台输出", StringComparison.Ordinal));

        PressCtrlB(shell);

        Assert.Contains(shell.View.Messages.Items.OfType<AssistantMessageItem>(),
            item => item.Text.Contains("后台输出", StringComparison.Ordinal));
    }

    /// <summary>后台查询自己结束（前台空闲）：记完成并提示，不抢前台 busy 状态。</summary>
    [Fact]
    public void BackgroundQueryCompletion_NotifiesWithoutTakingForegroundBusy()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "long task");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        shell.DrainEngine();
        PressCtrlB(shell);

        engine.Events.Enqueue(new BridgeEvent.TurnCompleted { QueryId = "q-test" });
        shell.DrainEngine();

        Assert.False(shell.IsBusy);
        Assert.Contains(shell.View.Messages.Items.OfType<SystemNoticeItem>(),
            item => item.Text.Contains("已结束", StringComparison.Ordinal));
    }

    /// <summary>闲且无后台查询：ctrl+b 不接管，仍是编辑域光标左移（基准 Task 上下文仅在任务运行时活跃）。</summary>
    [Fact]
    public void CtrlB_WhenIdleWithoutBackground_MovesCursorLeft()
    {
        var (shell, _) = CreateShell();
        Type(shell, "ab");
        var before = shell.Input.CursorCol;

        PressCtrlB(shell);

        Assert.Equal(before - 1, shell.Input.CursorCol);
        Assert.DoesNotContain(shell.View.Messages.Items.OfType<SystemNoticeItem>(),
            item => item.Text.Contains("后台", StringComparison.Ordinal));
    }

    /// <summary>TASK135 B4 — 切回后台查询要带归属头（与离屏快照同格式），
    /// 否则多查询交替切回时实时滚动区只是线性拼接，读者分不清哪段属于哪条查询。</summary>
    [Fact]
    public void FocusBackgroundQuery_EmitsAttributionHeader()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "long task");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        shell.DrainEngine();
        PressCtrlB(shell);

        engine.Events.Enqueue(new BridgeEvent.TokenDelta(5, "后台输出") { QueryId = "q-test" });
        shell.DrainEngine();
        PressCtrlB(shell);

        var notices = shell.View.Messages.Items.OfType<SystemNoticeItem>().Select(i => i.Text).ToList();
        Assert.Contains(notices, text => text.Contains("切回后台查询 q-test", StringComparison.Ordinal)
                                         && text.Contains("运行中", StringComparison.Ordinal));
    }

    /// <summary>已完成后台查询切回：补投结束后有明确的结束分隔，读者知道这条查询到此为止。</summary>
    [Fact]
    public void FocusCompletedBackgroundQuery_EmitsClosingSeparator()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "long task");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        shell.DrainEngine();
        PressCtrlB(shell);
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("后台输出") { QueryId = "q-test" });
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted { QueryId = "q-test" });
        shell.DrainEngine();

        PressCtrlB(shell);

        var notices = shell.View.Messages.Items.OfType<SystemNoticeItem>().Select(i => i.Text).ToList();
        Assert.Contains(notices, text => text.Contains("后台查询 q-test 已结束", StringComparison.Ordinal));
    }

    /// <summary>TASK135 C1 回归 — 切回**已完成**的后台查询后，前台身份必须释放：
    /// 否则下一次提交被当成「前台有主」静默转后台，等于吞掉用户输入。</summary>
    [Fact]
    public void FocusCompletedBackgroundQuery_NextSubmitStaysForeground()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "旧任务");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        shell.DrainEngine();
        PressCtrlB(shell);
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("旧回答") { QueryId = "q-test" });
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted { QueryId = "q-test" });
        shell.DrainEngine();

        PressCtrlB(shell); // 切回已完成的后台查询（只看结果）

        Type(shell, "新问题");
        shell.Submit();

        Assert.DoesNotContain(shell.View.Messages.Items.OfType<SystemNoticeItem>(),
            item => item.Text.Contains("新查询已提交到后台", StringComparison.Ordinal));
    }

    /// <summary>TASK135 C1 — 多条后台查询按「最近」逐个切回：每条的缓冲事件都要落回视图、互不串台。</summary>
    [Fact]
    public void MultipleBackgroundQueries_FocusEachInTurn()
    {
        var (shell, engine) = CreateShell();
        engine.QueryIds.Enqueue("q-a");
        engine.QueryIds.Enqueue("q-b");

        Type(shell, "任务 A");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-a" });
        shell.DrainEngine();
        PressCtrlB(shell); // A → 后台

        Type(shell, "任务 B");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-b" });
        shell.DrainEngine();
        PressCtrlB(shell); // B → 后台

        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("A 的输出") { QueryId = "q-a" });
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted { QueryId = "q-a" });
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("B 的输出") { QueryId = "q-b" });
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted { QueryId = "q-b" });
        shell.DrainEngine();

        Assert.DoesNotContain(shell.View.Messages.Items.OfType<AssistantMessageItem>(),
            item => item.Text.Contains("A 的输出", StringComparison.Ordinal));

        PressCtrlB(shell); // 最近 = B
        PressCtrlB(shell); // 次近 = A

        var rendered = shell.View.Messages.Items.OfType<AssistantMessageItem>()
            .Select(item => item.Text).ToList();
        Assert.Contains(rendered, text => text.Contains("B 的输出", StringComparison.Ordinal));
        Assert.Contains(rendered, text => text.Contains("A 的输出", StringComparison.Ordinal));
    }
}
