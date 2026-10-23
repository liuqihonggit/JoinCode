namespace NativeTui.Tests.Repl.Fullscreen;

/// <summary>
/// AlternateScreenController 单元测试（TASK066 C3）— 对齐基准 ink AlternateScreen.tsx（50/55 行）
/// + termio/dec.ts（45-60 行）序列契约：
/// 进入 = 1049h + 2J + H + 鼠标组合（1000h→1002h→1003h→1006h — dec.ts:51-55 顺序）；
/// 退出 = 鼠标组合反序（1006l→1003l→1002l→1000l — dec.ts:56-60）+ 1049l 恢复主屏；
/// 幂等防御（重复 Enter / 未激活 Exit 零输出）+ Toggle 翻转 + null 委托降级状态机照常。
/// </summary>
public sealed class AlternateScreenControllerTests
{
    /// <summary>进入完整序列 — 基准 AlternateScreen.tsx:50 "ENTER_ALT_SCREEN + \x1B[2J\x1B[H + ENABLE_MOUSE_TRACKING"。</summary>
    private const string EnterFull = "\x1b[?1049h\x1b[2J\x1b[H\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h";

    /// <summary>退出完整序列 — 基准 AlternateScreen.tsx:55 "DISABLE_MOUSE_TRACKING + EXIT_ALT_SCREEN"（进入严格反序）。</summary>
    private const string ExitFull = "\x1b[?1006l\x1b[?1003l\x1b[?1002l\x1b[?1000l\x1b[?1049l";

    /// <summary>捕获构造 — List 收集发射序列（断言面）。</summary>
    private static (AlternateScreenController Controller, List<string> Writes) NewController()
    {
        var writes = new List<string>();
        return (new AlternateScreenController(writes.Add), writes);
    }

    /// <summary>进入序列 — 1049 + 清屏 + 光标 home + 鼠标组合四模式顺序（dec.ts:51-55）。</summary>
    [Fact]
    public void Enter_WritesAltScreenClearHomeAndMouseTracking()
    {
        var (controller, writes) = NewController();

        controller.Enter();

        Assert.Equal([EnterFull], writes);
        Assert.True(controller.IsActive);
    }

    /// <summary>退出序列 — 鼠标组合反序禁用后 1049 恢复主屏（进入序的严格逆序 — dec.ts:56-60）。</summary>
    [Fact]
    public void Exit_WritesMouseDisableThenAltScreenExit_InReverseOrder()
    {
        var (controller, writes) = NewController();
        controller.Enter();
        writes.Clear();

        controller.Exit();

        Assert.Equal([ExitFull], writes);
        Assert.False(controller.IsActive);
    }

    /// <summary>进入幂等 — 已激活重复 Enter 零输出（防双进入序列残留）。</summary>
    [Fact]
    public void Enter_WhenActive_IdempotentNoOp()
    {
        var (controller, writes) = NewController();
        controller.Enter();
        writes.Clear();

        controller.Enter();

        Assert.Empty(writes);
        Assert.True(controller.IsActive);
    }

    /// <summary>退出幂等 — 未激活 Exit 零输出（WriteExit 兜底路径安全重入）。</summary>
    [Fact]
    public void Exit_WhenInactive_NoOutput()
    {
        var (controller, writes) = NewController();

        controller.Exit();

        Assert.Empty(writes);
        Assert.False(controller.IsActive);
    }

    /// <summary>Toggle 翻转 — 未激活进入 / 激活退出（对齐 TranscriptController.Toggle 形态）。</summary>
    [Fact]
    public void Toggle_FlipsEnterAndExit()
    {
        var (controller, writes) = NewController();

        controller.Toggle();

        Assert.True(controller.IsActive);
        Assert.Equal([EnterFull], writes);
        writes.Clear();

        controller.Toggle();

        Assert.False(controller.IsActive);
        Assert.Equal([ExitFull], writes);
    }

    /// <summary>往返对称 — Enter + Exit 后总输出 = 进入 + 退出两段（进出序列首尾相接互为逆序语义）。</summary>
    [Fact]
    public void EnterThenExit_RoundTripSequences()
    {
        var (controller, writes) = NewController();

        controller.Enter();
        controller.Exit();

        Assert.Equal([EnterFull, ExitFull], writes);
    }

    /// <summary>null 写委托降级 — 状态机照常运转不抛（Core 不持终端 IO 的降级契约）。</summary>
    [Fact]
    public void NullWriteAction_StateMachineStillOperates()
    {
        var controller = new AlternateScreenController(writeAction: null);

        controller.Enter();
        var activeAfterEnter = controller.IsActive;
        controller.Exit();

        Assert.True(activeAfterEnter);
        Assert.False(controller.IsActive);
    }
}
