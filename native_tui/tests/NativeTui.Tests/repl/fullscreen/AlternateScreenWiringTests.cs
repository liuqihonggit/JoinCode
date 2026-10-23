namespace NativeTui.Tests.Repl.Fullscreen;

/// <summary>
/// ReplShell 备用屏接线集成测试（TASK066 C3 — Harness 模式对齐 TerminalStatusWiringTests）：
/// transcript 进入触发备用屏序列（1049 + 清屏 + home + 鼠标组合）/ ctrl+o 与 q/Esc 两条退出路径
/// 均对称恢复主屏 / WriteExit 兜底退出 / 引擎死亡防御退出 / null controller 零输出（默认契约）。
/// </summary>
public sealed class AlternateScreenWiringTests
{
    /// <summary>进入完整序列 — 基准 AlternateScreen.tsx:50 + dec.ts:51-55。</summary>
    private const string EnterFull = "\x1b[?1049h\x1b[2J\x1b[H\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h";

    /// <summary>退出完整序列 — 基准 AlternateScreen.tsx:55 + dec.ts:56-60（进入严格反序）。</summary>
    private const string ExitFull = "\x1b[?1006l\x1b[?1003l\x1b[?1002l\x1b[?1000l\x1b[?1049l";

    private sealed class Harness
    {
        public readonly FakeReplEngine Engine = new();
        public readonly StringWriter ShellOutput = new();
        public readonly StringWriter AltOutput = new();
        public ReplShell Shell = null!;

        /// <summary>构造 — withController=false 时装配 null（现役 inline 模式零破坏契约面）。</summary>
        public static Harness Create(bool withController = true)
        {
            var harness = new Harness();
            var controller = withController
                ? new AlternateScreenController(harness.AltOutput.Write)
                : null;
            var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
            harness.Shell = new ReplShell(
                harness.Engine, banner, harness.ShellOutput,
                sizeSource: new FakeTerminalSizeSource(80, 24),
                alternateScreen: controller);
            harness.Shell.SeedContent();
            return harness;
        }

        /// <summary>备用屏序列捕获通道（controller writeAction 注入处 — Core 不持终端 IO）。</summary>
        public string AltSequences => AltOutput.ToString();

        /// <summary>ReplShell 自身输出通道（WriteExit 退出序列等）。</summary>
        public string ShellOut => ShellOutput.ToString();
    }

    /// <summary>ctrl+o 进入 transcript — 备用屏完整进入序列 + 控制器激活。</summary>
    [Fact]
    public void TranscriptToggle_EntersAltScreenSequence()
    {
        var harness = Harness.Create();
        SeedMessages(harness.Engine, 1);
        harness.Shell.DrainEngine();

        CtrlO(harness.Shell);

        Assert.Contains(EnterFull, harness.AltSequences);
        Assert.True(harness.Shell.AlternateScreen!.IsActive);
    }

    /// <summary>ctrl+o 进出往返 — 备用屏对称退出（鼠标组合反序 + 1049l 恢复主屏）+ 控制器复位。</summary>
    [Fact]
    public void TranscriptToggleRoundTrip_ExitsAltScreenAndRestoresMainScreen()
    {
        var harness = Harness.Create();
        SeedMessages(harness.Engine, 1);
        harness.Shell.DrainEngine();
        CtrlO(harness.Shell);

        CtrlO(harness.Shell);

        Assert.Contains(ExitFull, harness.AltSequences);
        Assert.False(harness.Shell.AlternateScreen!.IsActive);
    }

    /// <summary>q 退出（Controller 内部退出路径 — 不经过 ToggleTranscript）— HandleTranscriptKey
    /// 收口后备用屏仍对称退出（wasActive 捕获比对）。</summary>
    [Fact]
    public void TranscriptQExit_ControllerInternalPath_ExitsAltScreen()
    {
        var harness = Harness.Create();
        SeedMessages(harness.Engine, 1);
        harness.Shell.DrainEngine();
        CtrlO(harness.Shell);

        Char(harness.Shell, 'q');

        Assert.False(harness.Shell.IsTranscriptMode);
        Assert.Contains(ExitFull, harness.AltSequences);
        Assert.False(harness.Shell.AlternateScreen!.IsActive);
    }

    /// <summary>WriteExit 兜底 — transcript 激活态直接退出（崩溃/异常路径宿主收尾），
    /// 备用屏对称退出恢复主屏（防残留备用屏）。</summary>
    [Fact]
    public void WriteExit_ExitsAltScreenSymmetrically()
    {
        var harness = Harness.Create();
        SeedMessages(harness.Engine, 1);
        harness.Shell.DrainEngine();
        CtrlO(harness.Shell);

        harness.Shell.WriteExit();

        Assert.Contains(ExitFull, harness.AltSequences);
        Assert.False(harness.Shell.AlternateScreen!.IsActive);
    }

    /// <summary>引擎死亡防御 — transcript 激活期引擎退出，DrainEngine 防御块先退备用屏
    /// 恢复主屏再上屏退出消息。</summary>
    [Fact]
    public void EngineDeath_ExitsAltScreenDefensively()
    {
        var harness = Harness.Create();
        SeedMessages(harness.Engine, 1);
        harness.Shell.DrainEngine();
        CtrlO(harness.Shell);

        harness.Engine.Alive = false;
        harness.Shell.DrainEngine();

        Assert.False(harness.Shell.IsTranscriptMode);
        Assert.Contains(ExitFull, harness.AltSequences);
    }

    /// <summary>null controller 零行为变化 — 默认构造（不注入）transcript 进出 + WriteExit，
    /// 全程无备用屏/鼠标跟踪序列（现役 inline 视口模式契约兜底）。</summary>
    [Fact]
    public void NullController_ZeroAltScreenSequences()
    {
        var harness = Harness.Create(withController: false);
        SeedMessages(harness.Engine, 1);
        harness.Shell.DrainEngine();

        CtrlO(harness.Shell);
        CtrlO(harness.Shell);
        harness.Shell.WriteExit();

        Assert.Null(harness.Shell.AlternateScreen);
        Assert.DoesNotContain("\x1b[?1049", harness.ShellOut);
        // 1000/1002/1003/1004/1006 前缀一并排除（1004 未注入 coordinator 本就不发 — 零输出总契约）
        Assert.DoesNotContain("\x1b[?10", harness.ShellOut);
    }

    private static void CtrlO(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\u000f', ConsoleKey.O, false, false, false));

    private static void Char(ReplShell shell, char c) =>
        shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, char.IsUpper(c), false, false));

    private static void SeedMessages(FakeReplEngine engine, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            engine.Events.Enqueue(new BridgeEvent.AssistantMessage($"回复 {i}", MessageId: i));
        }
    }
}
