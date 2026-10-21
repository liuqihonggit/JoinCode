namespace NativeTui.Tests.Repl;

/// <summary>
/// ctrl+o 全局 transcript 模式测试（M8-3 — TASK064，基准 app:toggleTranscript）：
/// ctrl+o 双向切换 prompt↔transcript 全屏模态（第六交互态）；
/// 进入 = 全部 CoT 消息展开（verbose）+ footer 专用提示；退出 = 全部收起 + footer 还原；
/// transcript 态吞全部按键（j/k/g/G/ctrl+u/ctrl+d 滚动、/ 搜索、ctrl+o 退出 — 字符不进输入框）；
/// / 搜索：字符累积查询 + Enter 跳转到下一个匹配项（滚动定位）+ Esc 清除。
/// </summary>
public sealed class TranscriptModeTests
{
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell() {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"), new StringWriter(), sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        return (shell, engine);
    }

    private static void CtrlO(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\x0f', ConsoleKey.O, false, false, false));

    private static void Type(ReplShell shell, string text) {
        foreach (var ch in text) {
            shell.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
        }
    }

    /// <summary>两条带 CoT 消息 → ctrl+o 进入 transcript → 全部展开 + footer 提示。</summary>
    [Fact]
    public void CtrlO_EntersTranscriptMode_AllCoTExpanded_FooterHint()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("第一条", MessageId: 1, ThinkingDurationMs: 100, ThinkingContent: "CoT1"));
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("第二条", MessageId: 2, ThinkingDurationMs: 200, ThinkingContent: "CoT2"));
        shell.DrainEngine();

        CtrlO(shell);

        Assert.True(shell.IsTranscriptMode);
        var items = shell.View.Messages.Items.OfType<AssistantMessageItem>().ToList();
        Assert.True(items[0].IsThinkingExpanded);
        Assert.True(items[1].IsThinkingExpanded);
        Assert.Contains("详细转录", shell.View.Footer.Notification);
    }

    /// <summary>再次 ctrl+o 退出 → 全部收起 + footer 还原（Notification 恢复进入前值）。</summary>
    [Fact]
    public void CtrlO_Again_Exits_AllCollapsed_FooterRestored()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("带思考", MessageId: 1, ThinkingDurationMs: 100, ThinkingContent: "CoT"));
        shell.DrainEngine();

        CtrlO(shell);
        CtrlO(shell);

        Assert.False(shell.IsTranscriptMode);
        Assert.False(shell.View.Messages.Items.OfType<AssistantMessageItem>().Single().IsThinkingExpanded);
        Assert.DoesNotContain("详细转录", shell.View.Footer.Notification ?? string.Empty);
    }

    /// <summary>无 CoT 消息 ctrl+o — 模式仍切换（verbose 全屏可看全文），无展开项不报错。</summary>
    [Fact]
    public void CtrlO_NoCoT_StillTogglesMode()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("无思考回复"));
        shell.DrainEngine();

        CtrlO(shell);

        Assert.True(shell.IsTranscriptMode);
        Assert.False(shell.View.Messages.Items.OfType<AssistantMessageItem>().Single().IsThinkingExpanded);
    }

    /// <summary>transcript 态吞按键 — 字符 j/k 不进输入框（模态语义守卫）。</summary>
    [Fact]
    public void TranscriptMode_SwallowsCharacterKeys_NotIntoInput()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("回复", MessageId: 1));
        shell.DrainEngine();
        CtrlO(shell);

        Type(shell, "jkx");

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.True(shell.IsTranscriptMode);
    }

    /// <summary>/ 搜索 — 字符累积查询 + footer 显示查询 + Esc 清除回 transcript。</summary>
    [Fact]
    public void TranscriptSearch_TypeAccumulates_EscClears()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("错误修复完成"));
        shell.DrainEngine();
        CtrlO(shell);

        shell.HandleKey(new ConsoleKeyInfo('/', ConsoleKey.D1, false, false, false));
        Type(shell, "错误");
        Assert.Contains("错误", shell.View.Footer.Notification);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        Assert.DoesNotContain("错误", shell.View.Footer.Notification ?? string.Empty);
        Assert.True(shell.IsTranscriptMode);
    }

    /// <summary>/ 搜索 Enter — 跳转到下一个含查询的滚动项（ScrollTop 移向匹配项）。</summary>
    [Fact]
    public void TranscriptSearch_Enter_ScrollsToNextMatch()
    {
        var (shell, engine) = CreateShell();
        for (var i = 1; i <= 30; i++) {
            engine.Events.Enqueue(new BridgeEvent.AssistantMessage($"普通回复 {i}", MessageId: i));
        }
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("目标关键词在这里", MessageId: 31));
        shell.DrainEngine();
        CtrlO(shell);
        var before = shell.View.Messages.ScrollTop;

        shell.HandleKey(new ConsoleKeyInfo('/', ConsoleKey.D1, false, false, false));
        Type(shell, "目标关键词");
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.True(shell.View.Messages.ScrollTop > before, $"Enter 应跳向匹配项（{before} → {shell.View.Messages.ScrollTop}）");
    }

    /// <summary>transcript 态 Esc — 退出 transcript（非搜索态时）。</summary>
    [Fact]
    public void TranscriptMode_Esc_ExitsMode()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("回复", MessageId: 1));
        shell.DrainEngine();
        CtrlO(shell);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.False(shell.IsTranscriptMode);
    }
}
