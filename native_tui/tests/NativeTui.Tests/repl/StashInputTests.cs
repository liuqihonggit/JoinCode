namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell ctrl+s 输入暂存测试（TASK064 P3 — 基准 defaultBindings.ts:85 ctrl+s → chat:stash +
/// PromptInputStashNotice.tsx:18 单槽暂存语义）：忙/闲时暂存（输入清空 + dim 反馈消息上屏 +
/// 单槽记录）、重复暂存覆盖旧槽、空输入无操作（不覆盖已有暂存）、提交后自动恢复
///（排队路径 + 直发路径 — 提交动作发生后暂存文本回填输入框）、无暂存时提交不恢复。
/// 构造形态对齐 ReplShellPanelNavigationTests / ReplShellIntegrationStopTaskTests。
/// </summary>
public sealed class StashInputTests
{
    /// <summary>构造测试 shell（默认登录门开 — 直发路径可达）。</summary>
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

    /// <summary>ctrl+s 注入（ConsoleKeyInfo 形态 — ConsoleKeyAdapter 归一 Ctrl+字母）。</summary>
    private static void CtrlS(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('s', ConsoleKey.S, shift: false, alt: false, control: true));

    /// <summary>Enter 提交注入。</summary>
    private static void Enter(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

    /// <summary>逐字符打字注入（形态对齐 ReplShellIntegrationStopTaskTests.Char）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, shift: false, alt: false, control: false));
        }
    }

    /// <summary>驱动进入忙时回合（TurnStarted 事件排空投影）。</summary>
    private static void MakeBusy(ReplShell shell, FakeReplEngine engine)
    {
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
    }

    /// <summary>忙时 ctrl+s — 输入清空 + dim 反馈消息上屏 + 暂存单槽记录。</summary>
    [Fact]
    public void Stash_WhileBusy_ClearsInputPostsFeedbackAndRecords()
    {
        var (shell, engine) = CreateShell();
        MakeBusy(shell, engine);
        Type(shell, "abc");

        CtrlS(shell);

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Equal("abc", shell.StashedInput);
        var feedback = shell.View.Messages.Items.OfType<AssistantMessageItem>().Single();
        Assert.Equal("已暂存（提交后自动恢复）", feedback.Text);
    }

    /// <summary>闲时 ctrl+s 同样暂存（暂存非忙时专属 — 语义=收起当前输入腾出输入框）。</summary>
    [Fact]
    public void Stash_WhileIdle_ClearsInputAndRecords()
    {
        var (shell, _) = CreateShell();
        Type(shell, "draft");

        CtrlS(shell);

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Equal("draft", shell.StashedInput);
        Assert.Contains(shell.View.Messages.Items.OfType<AssistantMessageItem>(), m => m.Text == "已暂存（提交后自动恢复）");
    }

    /// <summary>重复 ctrl+s 覆盖旧暂存（单槽语义 — 非栈）。</summary>
    [Fact]
    public void Stash_Twice_OverwritesSingleSlot()
    {
        var (shell, _) = CreateShell();
        Type(shell, "first");
        CtrlS(shell);
        Type(shell, "second");

        CtrlS(shell);

        Assert.Equal("second", shell.StashedInput);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>空输入 ctrl+s — 无操作（无反馈消息 + 不覆盖已有暂存槽）。</summary>
    [Fact]
    public void Stash_EmptyInput_IsNoOpAndKeepsExistingSlot()
    {
        var (shell, _) = CreateShell();
        Type(shell, "abc");
        CtrlS(shell);

        CtrlS(shell);

        Assert.Equal("abc", shell.StashedInput);
        Assert.Equal(string.Empty, shell.Input.Text);
        // 空输入无操作 — 反馈消息只此一条（第二次 ctrl+s 未追加）
        Assert.Equal(1, shell.View.Messages.Items.OfType<AssistantMessageItem>().Count(m => m.Text == "已暂存（提交后自动恢复）"));
    }

    /// <summary>忙时暂存 → 忙时提交（排队路径）— 排队文本入队 + 暂存文本回填输入框 + 单槽清空。</summary>
    [Fact]
    public void Submit_WhileBusy_RestoresStashedAfterQueueing()
    {
        var (shell, engine) = CreateShell();
        MakeBusy(shell, engine);
        Type(shell, "abc");
        CtrlS(shell);
        Type(shell, "def");

        Enter(shell);

        // 排队路径 — 引擎零直发，def 入队
        Assert.Empty(engine.Submits);
        Assert.Contains("def", shell.View.QueuedInputs);
        // 提交动作已发生 — 暂存回填 + 单槽清空
        Assert.Equal("abc", shell.Input.Text);
        Assert.Equal(string.Empty, shell.StashedInput);
    }

    /// <summary>暂存 → 闲时提交（直发路径）— 引擎收到提交 + 暂存文本回填输入框 + 单槽清空。</summary>
    [Fact]
    public void Submit_WhileIdle_RestoresStashedAfterDirectSend()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "abc");
        CtrlS(shell);
        Type(shell, "def");

        Enter(shell);

        Assert.Equal(["def"], engine.Submits);
        Assert.Equal("abc", shell.Input.Text);
        Assert.Equal(string.Empty, shell.StashedInput);
    }

    /// <summary>无暂存时提交 — 输入框保持清空（不恢复不误填）。</summary>
    [Fact]
    public void Submit_WithoutStash_KeepsInputCleared()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "def");

        Enter(shell);

        Assert.Equal(["def"], engine.Submits);
        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Equal(string.Empty, shell.StashedInput);
    }
}
