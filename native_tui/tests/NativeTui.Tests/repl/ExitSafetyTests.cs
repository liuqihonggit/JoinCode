namespace NativeTui.Tests.Repl;

/// <summary>
/// 退出安全按键测试（TASK059 — 用户痛点：Esc/Ctrl+C 误触直接关闭终端）—
/// Esc 永不退出（忙时 500ms 双击中断，单击等窗口；闲时清输入）；
/// Ctrl+C 复制最后 assistant 消息（剪贴板委托注入，零依赖 ADR 0140）；
/// 退出路径只剩 Ctrl+D（空输入双击确认 — S6 对齐基准 useExitOnCtrlCD）与 /exit 命令。
/// </summary>
public sealed class ExitSafetyTests
{
    /// <summary>可控时钟 — 逐击推进（每击 +100ms，双击窗口 500ms 内）。</summary>
    private sealed class ManualClock {
        private long _ms;
        public long Tick() => _ms += 100;
        public void Advance(int ms) => _ms += ms;
    }

    /// <summary>构造外壳 + 假引擎 + 可控 Esc 时钟（busy 经 TurnStarted 事件驱动；SeedContent 对齐 Run 首步挂 spinner）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine, ManualClock Clock) CreateShell(Action<string>? clipboard = null) {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
        var shell = new ReplShell(engine, banner, new StringWriter(), clipboard: clipboard);
        shell.SeedContent();
        var clock = new ManualClock();
        shell.EscClock = clock.Tick;
        return (shell, engine, clock);
    }

    private static ConsoleKeyInfo Esc() => new('\0', ConsoleKey.Escape, false, false, false);
    private static ConsoleKeyInfo CtrlC() => new('\x03', ConsoleKey.C, false, false, false);

    /// <summary>busy 置位（TurnStarted 事件）。</summary>
    private static void MakeBusy(ReplShell shell, FakeReplEngine engine) {
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
    }

    // ---- Esc 双击中断 ----

    /// <summary>忙时双击（500ms 内）→ Interrupt。</summary>
    [Fact]
    public void Busy_DoubleEscape_Interrupts()
    {
        var (shell, engine, _) = CreateShell();
        MakeBusy(shell, engine);

        shell.HandleKey(Esc());
        shell.HandleKey(Esc());

        Assert.Equal(1, engine.Interrupts);
    }

    /// <summary>忙时单击 → 无操作（等双击窗口，不打断不退出）。</summary>
    [Fact]
    public void Busy_SingleEscape_WaitsForSecondTap()
    {
        var (shell, engine, _) = CreateShell();
        MakeBusy(shell, engine);

        shell.HandleKey(Esc());

        Assert.Equal(0, engine.Interrupts);
        Assert.True(shell.IsRunning);
    }

    /// <summary>忙时两击超窗（>500ms）→ 不构成双击不 Interrupt（各自孤立单击）。</summary>
    [Fact]
    public void Busy_EscapeOutsideDoubleClickWindow_NoInterrupt()
    {
        var (shell, engine, clock) = CreateShell();
        MakeBusy(shell, engine);

        shell.HandleKey(Esc());
        clock.Advance(6000);
        shell.HandleKey(Esc());

        Assert.Equal(0, engine.Interrupts);
        Assert.True(shell.IsRunning);
    }

    /// <summary>闲时空输入连击 Esc → 永不退出（原行为 _running=false 移除）。</summary>
    [Fact]
    public void Idle_EscapeEmptyInput_NeverExits()
    {
        var (shell, engine, _) = CreateShell();

        shell.HandleKey(Esc());
        shell.HandleKey(Esc());
        shell.HandleKey(Esc());

        Assert.True(shell.IsRunning);
        Assert.Equal(0, engine.Interrupts);
    }

    /// <summary>闲时有文本 Esc → 清空输入（既有行为保留，不退出）。</summary>
    [Fact]
    public void Idle_EscapeWithText_ClearsInputNotExit()
    {
        var (shell, _, _) = CreateShell();
        shell.HandleKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));

        shell.HandleKey(Esc());

        Assert.Equal(string.Empty, shell.View.Input);
        Assert.True(shell.IsRunning);
    }

    // ---- Ctrl+C 复制 ----

    /// <summary>Ctrl+C → 剪贴板委托收到最后一条 assistant 消息文本。</summary>
    [Fact]
    public void CtrlC_CopiesLastAssistantMessage()
    {
        var copied = new List<string>();
        var (shell, engine, _) = CreateShell(copied.Add);
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("第一条回答"));
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("最后一条回答"));
        shell.DrainEngine();

        shell.HandleKey(CtrlC());

        Assert.Equal(["最后一条回答"], copied);
        Assert.True(shell.IsRunning);
    }

    /// <summary>Ctrl+C 无 assistant 消息 → 无复制无异常。</summary>
    [Fact]
    public void CtrlC_NoAssistantMessage_NoCopyNoThrow()
    {
        var copied = new List<string>();
        var (shell, _, _) = CreateShell(copied.Add);

        shell.HandleKey(CtrlC());

        Assert.Empty(copied);
        Assert.True(shell.IsRunning);
    }

    /// <summary>Ctrl+C 未注入剪贴板委托（Demo/测试）→ 无操作不抛。</summary>
    [Fact]
    public void CtrlC_NullClipboard_NoThrow()
    {
        var (shell, _, _) = CreateShell(clipboard: null);

        shell.HandleKey(CtrlC());

        Assert.True(shell.IsRunning);
    }

    // ---- 退出路径保留 ----

    /// <summary>Ctrl+D 空输入首按 — 不退出（双击确认链 S6：通知提示"再按一次"，窗口 3s）。</summary>
    [Fact]
    public void CtrlD_EmptyInput_FirstPress_DoesNotExit()
    {
        var (shell, _, _) = CreateShell();

        shell.HandleKey(new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, false));

        Assert.True(shell.IsRunning);
        Assert.Equal(1, shell.View.Footer.Notifications!.QueuedCount);
    }

    /// <summary>Ctrl+D 空输入双击（确认窗口内）→ 退出（唯一按键退出路径 — 双击确认）。</summary>
    [Fact]
    public void CtrlD_EmptyInput_DoublePress_Exits()
    {
        var (shell, _, _) = CreateShell();

        shell.HandleKey(new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, false));

        Assert.False(shell.IsRunning);
    }

    /// <summary>Ctrl+D 确认窗口过期（>3s）— 二按视为新首按不退出（重新计提示）。</summary>
    [Fact]
    public void CtrlD_WindowExpired_SecondPress_RestartsConfirmation()
    {
        var (shell, _, clock) = CreateShell();

        shell.HandleKey(new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, false));
        clock.Advance(ReplShell.ExitConfirmWindowMs + 1);
        shell.HandleKey(new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, false));

        Assert.True(shell.IsRunning);
    }

    /// <summary>Ctrl+D 有文本 → 删除光标字符不退出（既有行为保留）。</summary>
    [Fact]
    public void CtrlD_WithText_DeletesNotExits()
    {
        var (shell, _, _) = CreateShell();
        shell.HandleKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));

        shell.HandleKey(new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, false));

        Assert.True(shell.IsRunning);
    }
}
