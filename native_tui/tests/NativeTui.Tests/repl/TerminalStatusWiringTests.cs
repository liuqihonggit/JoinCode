namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 终端状态接线测试（A5-A8 连线 — 三态源驱动 + 焦点拦截 + 失焦冻结 + 退出对称清理）：
/// 事件流（TurnStarted/TurnCompleted）驱动三路输出；权限/命令对话框挂起归并 waiting（优先级&gt;busy）；
/// FocusIn/FocusOut 键吞掉不分派（不插字符）+ 冻结动画时钟（AnimTimeMs 停推进）；
/// WriteExit 尾对称：Disable(DEC 1004) + 标题空 + CLEAR_TAB_STATUS。
/// 默认构造（不注入 coordinator）零行为变化 — 现有测试全绿兜底。
/// </summary>
public sealed class TerminalStatusWiringTests
{
    private sealed class Harness
    {
        public readonly FakeReplEngine Engine = new();
        public readonly StringWriter Output = new();
        public readonly StringWriter ShellOutput = new();
        public readonly List<string> TitleWrites = [];
        public ReplShell Shell = null!;

        public static Harness Create(bool tabStatus = true, bool progress = true)
        {
            var harness = new Harness();
            var tracker = new TerminalTitleTracker("JoinCode", harness.TitleWrites.Add);
            var coordinator = new TerminalStatusCoordinator(harness.Output, tracker, tabStatus, progress);
            var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
            harness.Shell = new ReplShell(
                harness.Engine, banner, harness.ShellOutput,
                sizeSource: new FakeTerminalSizeSource(80, 24),
                terminalStatus: coordinator);
            harness.Shell.SeedContent();
            return harness;
        }

        public void MakeBusy()
        {
            Engine.Events.Enqueue(new BridgeEvent.TurnStarted());
            Shell.DrainEngine();
        }

        public void CompleteTurn()
        {
            Engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
            Shell.DrainEngine();
        }

        public string TerminalOutput => Output.ToString();

        /// <summary>WriteExit 序列通道（ReplShell 自身 output — DEC 1004 Disable 等退出序列写此处）。</summary>
        public string ExitOutput => ShellOutput.ToString();
    }

    [Fact]
    public void TurnStarted_DrivesBusyTitleTabAndProgress()
    {
        var harness = Harness.Create();
        harness.Shell.RenderAndFlush();
        harness.MakeBusy();
        harness.Shell.RenderAndFlush();

        Assert.Equal("⠂ JoinCode", harness.TitleWrites[^1]);
        Assert.Contains("indicator=#ff9500;status=Working…", harness.TerminalOutput);
        Assert.Contains("\x1b]9;4;3;0\x07", harness.TerminalOutput);
    }

    [Fact]
    public void TurnCompleted_DrivesIdleAndClearsProgress()
    {
        var harness = Harness.Create();
        harness.MakeBusy();
        harness.Shell.RenderAndFlush();
        harness.CompleteTurn();
        harness.Shell.RenderAndFlush();

        Assert.Equal("✳ JoinCode", harness.TitleWrites[^1]);
        Assert.Contains("indicator=#00d75f;status=Idle", harness.TerminalOutput);
        Assert.Contains("\x1b]9;4;0;0\x07", harness.TerminalOutput);
    }

    [Fact]
    public void PermissionPending_MergesWaitingOverBusy()
    {
        // 权限挂起 = waiting（优先级 > busy — 基准 REPL.tsx:1187 isWaitingForApproval 优先判定）
        var harness = Harness.Create();
        harness.MakeBusy();
        harness.Shell.RenderAndFlush();
        harness.Shell.Permission.Begin(new PermissionRequest("Read", "a.cs 预览"));
        harness.Shell.RenderAndFlush();

        Assert.Contains("indicator=#5f87ff;status=Waiting", harness.TerminalOutput);
        // waiting 挂起不清进度 — 回合仍在进行
        Assert.DoesNotContain("\x1b]9;4;0;0\x07", harness.TerminalOutput);
    }

    [Fact]
    public void FocusOut_SwallowedAndFreezesAnimationClock()
    {
        var harness = Harness.Create();
        harness.MakeBusy();
        harness.Shell.RenderAndFlush();
        var animBefore = harness.Shell.AnimTimeMsInternal;

        harness.Shell.HandleRawKeyPress(KeyPress.Named(KeyKind.FocusOut));
        harness.Shell.RenderAndFlush();

        // 焦点键吞掉 — 不进输入缓冲（无字符插入）
        Assert.Equal(string.Empty, harness.Shell.Input.Text);
        // 动画时钟冻结（失焦帧不推进）
        Assert.Equal(animBefore, harness.Shell.AnimTimeMsInternal);
        // 标题动画暂停 — 帧冻结在 ⠂
        Assert.Equal("⠂ JoinCode", harness.TitleWrites[^1]);
    }

    [Fact]
    public void FocusIn_ResumesAnimationClock()
    {
        var harness = Harness.Create();
        harness.MakeBusy();
        harness.Shell.RenderAndFlush();
        harness.Shell.HandleRawKeyPress(KeyPress.Named(KeyKind.FocusOut));
        harness.Shell.RenderAndFlush();
        var frozen = harness.Shell.AnimTimeMsInternal;

        harness.Shell.HandleRawKeyPress(KeyPress.Named(KeyKind.FocusIn));
        harness.Shell.RenderAndFlush();

        Assert.True(harness.Shell.AnimTimeMsInternal > frozen);
    }

    [Fact]
    public void WriteExit_DisablesFocusReportingAndClearsAll()
    {
        var harness = Harness.Create();
        harness.MakeBusy();
        harness.Shell.RenderAndFlush();

        harness.Shell.WriteExit();

        Assert.Contains("\x1b[?1004l", harness.ExitOutput);
        Assert.Contains("\x1b]21337;indicator=;status=;status-color=\x07", harness.TerminalOutput);
        Assert.Equal(string.Empty, harness.TitleWrites[^1]);
    }

    [Fact]
    public void DefaultConstructor_NoTerminalStatusOutput()
    {
        // 默认不注入 coordinator — 零行为变化（既有测试契约兜底）
        var engine = new FakeReplEngine();
        var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
        var output = new StringWriter();
        var shell = new ReplShell(engine, banner, output, sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
        shell.RenderAndFlush();
        shell.WriteExit();

        Assert.DoesNotContain("21337", output.ToString());
        Assert.DoesNotContain("9;4", output.ToString());
        Assert.DoesNotContain("1004", output.ToString());
    }
}
