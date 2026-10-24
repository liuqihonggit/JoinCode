// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
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

    // ---- Esc 忙时打断（基准 useCancelRequest.ts:97-102 — abortSignal 未 aborted 时单击直接 onCancel） ----

    /// <summary>忙时单击 → 立即 Interrupt（基准单击即断 — 无双击确认层）。</summary>
    [Fact]
    public void Busy_SingleEscape_Interrupts()
    {
        var (shell, engine, _) = CreateShell();
        MakeBusy(shell, engine);

        shell.HandleKey(Esc());

        Assert.Equal(1, engine.Interrupts);
    }

    /// <summary>忙时孤立两击 → 各打断一次（无窗口语义，累加 2 次）。</summary>
    [Fact]
    public void Busy_TwoIsolatedEscapes_InterruptTwice()
    {
        var (shell, engine, clock) = CreateShell();
        MakeBusy(shell, engine);

        shell.HandleKey(Esc());
        clock.Advance(6000);
        shell.HandleKey(Esc());

        Assert.Equal(2, engine.Interrupts);
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

    /// <summary>闲时有文本 Esc 单击 → 不清空（防误触 — 首击仅提示通知）。</summary>
    [Fact]
    public void Idle_EscapeWithText_SingleTap_KeepsInput()
    {
        var (shell, _, _) = CreateShell();
        shell.HandleKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));

        shell.HandleKey(Esc());

        Assert.Equal("a", shell.Input.Text);
        Assert.True(shell.IsRunning);
    }

    /// <summary>闲时有文本 Esc 双击（800ms 窗口内）→ 清空 + 草稿入史可翻回
    ///（基准 useTextInput.ts:126-153 双击清空 + addToHistory 存档 — 误触不丢文本）。</summary>
    [Fact]
    public void Idle_EscapeWithText_DoubleTap_ClearsAndSavesHistory()
    {
        var (shell, engine, _) = CreateShell();
        shell.HandleKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));

        shell.HandleKey(Esc());
        shell.HandleKey(Esc());

        Assert.Equal(string.Empty, shell.View.Input);
        Assert.True(shell.IsRunning);
        Assert.Contains("ab", shell.History.EntryTexts);
    }

    // ---- Ctrl+C 打断/退出（基准 defaultBindings.ts:40 'ctrl+c': app:interrupt + useTextInput 双击退出） ----

    /// <summary>Ctrl+C 忙时 → 立即打断（基准 app:interrupt — abortSignal 打断当前回合）。</summary>
    [Fact]
    public void CtrlC_WhileBusy_Interrupts()
    {
        var (shell, engine, _) = CreateShell();
        MakeBusy(shell, engine);

        shell.HandleKey(CtrlC());

        Assert.Equal(1, engine.Interrupts);
        Assert.True(shell.IsRunning);
    }

    /// <summary>Ctrl+C 闲时双击（800ms 窗口内）→ 退出（复制语义移除 — 复制走消息光标态 c 键既有通道）。</summary>
    [Fact]
    public void CtrlC_IdleDoubleTap_Exits()
    {
        var (shell, _, _) = CreateShell();

        shell.HandleKey(CtrlC());
        Assert.True(shell.IsRunning);
        shell.HandleKey(CtrlC());

        Assert.False(shell.IsRunning);
    }

    // ---- 退出路径保留 ----

    /// <summary>Ctrl+D 空输入首按 — 不退出（双击确认链 S6：通知提示"再按一次"，窗口 800ms）。</summary>
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

    /// <summary>Ctrl+D 确认窗口过期（>800ms）— 二按视为新首按不退出（重新计提示）。</summary>
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
