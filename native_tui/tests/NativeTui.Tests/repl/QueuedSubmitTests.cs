namespace NativeTui.Tests.Repl;

/// <summary>
/// 一问一答节奏测试（TASK060 Step1 — 用户痛点：忙时提交被忽略致两条提问堆叠无回答夹中间）—
/// 忙时提交改排队（基准 queued 语义）：用户输入上屏 + ⎿ 已排队 dim 行（ToolResultItem 复用）、
/// 不调引擎；回合结束（TurnCompleted 后非忙态）自动按序发送队首（不重复上屏）。
/// </summary>
public sealed class QueuedSubmitTests
{
    /// <summary>构造外壳（SeedContent 对齐 Run 首步）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell() {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"), new StringWriter());
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>忙时提交文本（HandleKey Return 路径 — 对齐用户真实按键链）。</summary>
    private static void TypeAndSubmit(ReplShell shell, string text) {
        foreach (var ch in text)
            shell.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    /// <summary>busy 置位 / 回合结束（TurnStarted → TurnCompleted 事件对）。</summary>
    private static void MakeBusy(ReplShell shell, FakeReplEngine engine) {
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
    }

    private static void EndTurn(ReplShell shell, FakeReplEngine engine) {
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
        shell.DrainEngine();
    }

    /// <summary>忙时提交 — 上屏用户输入 + ⎿ 已排队行，不调引擎。</summary>
    [Fact]
    public void BusySubmit_QueuesLocallyWithoutEngineCall()
    {
        var (shell, engine) = CreateShell();
        MakeBusy(shell, engine);

        TypeAndSubmit(shell, "3333");

        Assert.Empty(engine.Submits);
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem u && u.Text == "3333");
        Assert.Contains(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("已排队"));
    }

    /// <summary>回合结束 — 队首自动发送（不重复上屏用户消息）。</summary>
    [Fact]
    public void TurnCompleted_AutoSendsQueuedInput()
    {
        var (shell, engine) = CreateShell();
        MakeBusy(shell, engine);
        TypeAndSubmit(shell, "3333");
        Assert.Single(shell.View.Messages.Items.OfType<UserMessageItem>());

        EndTurn(shell, engine);

        Assert.Equal(["3333"], engine.Submits);
        Assert.Single(shell.View.Messages.Items.OfType<UserMessageItem>());
    }

    /// <summary>多条排队 — 按序逐回合发送（一问一答节奏）。</summary>
    [Fact]
    public void MultipleQueuedInputs_SendInOrderPerTurn()
    {
        var (shell, engine) = CreateShell();
        MakeBusy(shell, engine);
        TypeAndSubmit(shell, "第一条");
        TypeAndSubmit(shell, "第二条");

        EndTurn(shell, engine);
        Assert.Equal(["第一条"], engine.Submits);

        MakeBusy(shell, engine);
        EndTurn(shell, engine);
        Assert.Equal(["第一条", "第二条"], engine.Submits);
    }

    /// <summary>闲时提交 — 正常直发（排队不误拦闲态）。</summary>
    [Fact]
    public void IdleSubmit_SendsDirectly()
    {
        var (shell, engine) = CreateShell();

        TypeAndSubmit(shell, "正常输入");

        Assert.Equal(["正常输入"], engine.Submits);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is ToolResultItem t && t.Text.Contains("已排队"));
    }

    /// <summary>排队文本 — 入历史环（与正常提交同语义，↑ 可翻阅）。</summary>
    [Fact]
    public void QueuedInput_EntersHistoryRing()
    {
        var (shell, engine) = CreateShell();
        MakeBusy(shell, engine);

        TypeAndSubmit(shell, "排队也入历史");

        Assert.Contains("排队也入历史", shell.History.Entries);
    }

    /// <summary>排队上限守卫 — 满 10 条后新输入丢弃（⎿ 反馈），不调引擎不无限膨胀。</summary>
    [Fact]
    public void QueuedInputs_OverLimit_DroppedWithFeedback()
    {
        var (shell, engine) = CreateShell();
        MakeBusy(shell, engine);
        for (var i = 1; i <= ReplShell.MaxQueuedInputs; i++)
        {
            TypeAndSubmit(shell, $"第{i}条");
        }

        TypeAndSubmit(shell, "超限条");

        var notes = shell.View.Messages.Items.OfType<ToolResultItem>().ToList();
        Assert.Contains(notes, t => t.Text.Contains("排队已满"));
        Assert.DoesNotContain(notes.Where(t => t.Text.Contains("排队已满")), t => t.Text.Contains("自动发送"));

        EndTurn(shell, engine);
        for (var i = 1; i < ReplShell.MaxQueuedInputs; i++)
        {
            shell.DrainEngine();
        }
        Assert.Equal(Enumerable.Range(1, ReplShell.MaxQueuedInputs).Select(i => $"第{i}条"), engine.Submits);
    }

    /// <summary>上限内排队 — 正常入队（边界值恰好 10 条不误拦）。</summary>
    [Fact]
    public void QueuedInputs_AtLimit_AllQueued()
    {
        var (shell, engine) = CreateShell();
        MakeBusy(shell, engine);
        for (var i = 1; i <= ReplShell.MaxQueuedInputs; i++)
        {
            TypeAndSubmit(shell, $"第{i}条");
        }

        var notes = shell.View.Messages.Items.OfType<ToolResultItem>().ToList();
        Assert.Equal(ReplShell.MaxQueuedInputs, notes.Count(t => t.Text.Contains("已排队")));
        Assert.DoesNotContain(notes, t => t.Text.Contains("排队已满"));
    }
}
