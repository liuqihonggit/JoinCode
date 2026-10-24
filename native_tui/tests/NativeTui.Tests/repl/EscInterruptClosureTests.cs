// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Tests.Repl;

/// <summary>
/// Esc 打断闭环测试（M8-2 — TASK064，基准 REPL.tsx onCancel + auto-restore）：
/// ①打断后无有效响应到达 → 自动恢复用户输入到输入框 + 移除该条用户消息 + 撤销历史条目（基准 rewind 近似）；
/// ②有 partial 响应 → 消息定格保留、不恢复输入（M8-1 兜底衔接）；
/// ③忙时有排队输入 → 单击 Esc 全部弹回输入框（基准 popAllCommandsFromQueue — 优先于双击打断判定）。
/// </summary>
public sealed class EscInterruptClosureTests
{
    /// <summary>可控时钟 — 逐击推进（每击 +100ms，双击窗口 500ms 内）。</summary>
    private sealed class ManualClock {
        private long _ms;
        public long Tick() => _ms += 100;
        public void Advance(int ms) => _ms += ms;
    }

    private static (ReplShell Shell, FakeReplEngine Engine, ManualClock Clock) CreateShell() {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
        var shell = new ReplShell(engine, banner, new StringWriter());
        shell.SeedContent();
        var clock = new ManualClock();
        shell.EscClock = clock.Tick;
        return (shell, engine, clock);
    }

    private static ConsoleKeyInfo Esc() => new('\0', ConsoleKey.Escape, false, false, false);

    private static void TypeAndSubmit(ReplShell shell, string text) {
        foreach (var ch in text) {
            shell.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
        }
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    /// <summary>首条直发后置 busy（TurnStarted drain — 后续提交走排队路径）。</summary>
    private static void MakeBusy(ReplShell shell, FakeReplEngine engine) {
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
    }

    private static void RunTurn(FakeReplEngine engine, ReplShell shell, params BridgeEvent[] events) {
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        foreach (var bridgeEvent in events) {
            engine.Events.Enqueue(bridgeEvent);
        }
        engine.Events.Enqueue(new BridgeEvent.TurnInterrupted());
        shell.DrainEngine();
    }

    /// <summary>打断后无任何有效响应（无正文/无工具/无流式）→ 输入恢复 + 用户消息移除 + 历史撤销。</summary>
    [Fact]
    public void Interrupt_NoResponseArrived_RestoresInputAndRemovesUserMessage()
    {
        var (shell, engine, _) = CreateShell();
        TypeAndSubmit(shell, "帮我查一下");

        RunTurn(engine, shell);

        Assert.Equal("帮我查一下", shell.Input.Text);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is UserMessageItem);
        Assert.False(shell.History.EntryTexts.Contains("帮我查一下"), "历史条目应被撤销");
    }

    /// <summary>打断后有 partial 响应（流式增量已到达）→ 消息定格保留 + 输入不恢复 + 历史保留（基准 partial 语义）。</summary>
    [Fact]
    public void Interrupt_PartialResponseArrived_KeepsMessages_NoRestore()
    {
        var (shell, engine, _) = CreateShell();
        TypeAndSubmit(shell, "帮我查一下");

        RunTurn(engine, shell, new BridgeEvent.TokenDelta(3, "部分"));

        var partial = Assert.Single(shell.View.Messages.Items.OfType<AssistantMessageItem>());
        Assert.Equal("部分", partial.Text);
        Assert.Empty(shell.Input.Text);
        Assert.Contains("帮我查一下", shell.History.EntryTexts);
    }

    /// <summary>打断前有完整响应到达 → 不恢复（正常完成形态的打断近似）。</summary>
    [Fact]
    public void Interrupt_FullResponseArrived_NoRestore()
    {
        var (shell, engine, _) = CreateShell();
        TypeAndSubmit(shell, "帮我查一下");

        RunTurn(engine, shell,
            new BridgeEvent.TokenDelta(4, "完整回复"),
            new BridgeEvent.AssistantMessage("完整回复", MessageId: 1));

        Assert.Single(shell.View.Messages.Items.OfType<AssistantMessageItem>());
        Assert.Empty(shell.Input.Text);
        Assert.Contains("帮我查一下", shell.History.EntryTexts);
    }

    /// <summary>忙时有排队输入 → 单击 Esc 全部弹回输入框（按行拼接）+ 清空队列 + 不打断（基准重载链⑤优先）。</summary>
    [Fact]
    public void Busy_Escape_WithQueuedInputs_PopsAllBackToInput_NoInterrupt()
    {
        var (shell, engine, _) = CreateShell();
        TypeAndSubmit(shell, "第一条");
        MakeBusy(shell, engine);
        TypeAndSubmit(shell, "第二条");
        TypeAndSubmit(shell, "第三条");
        Assert.Equal(["第二条", "第三条"], shell.View.QueuedInputs);

        shell.HandleKey(Esc());

        Assert.Equal("第二条\n第三条", shell.Input.Text);
        Assert.Empty(shell.View.QueuedInputs);
        Assert.Equal(0, engine.Interrupts);
    }

    /// <summary>忙时无排队 → 单击 Esc 打断（对齐基准 useCancelRequest 单击 onCancel — 队列弹回不得破坏打断主路径）。</summary>
    [Fact]
    public void Busy_DoubleEscape_NoQueue_StillInterrupts()
    {
        var (shell, engine, _) = CreateShell();
        TypeAndSubmit(shell, "唯一一条");
        MakeBusy(shell, engine);

        shell.HandleKey(Esc());

        Assert.Equal(1, engine.Interrupts);
    }
}
