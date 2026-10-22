namespace NativeTui.Tests.Terminal;

/// <summary>
/// DEC 2026 同步输出回退测试 — FrameComposer 条件分支（S9）：
/// 终端不支持时跳过 BSU/ESU 包裹直出差分（对齐基准 writeDiffToTerminal skipSyncMarkers）；
/// 支持时/默认构造保持原子包裹（现行为零破坏）。
/// 组合链路（探测 → 构造）一并验证 — ReplShell 生产接线形态（未决问题）的契约预演。
/// </summary>
public sealed class TerminalSyncOutputFallbackTests
{
    [Fact]
    public void Compose_Unsupported_NoSyncWrapping()
    {
        using var screen = new Screen(4, 1);
        screen.WriteText(0, 0, "hi");
        var output = new FrameComposer(synchronizedOutputSupported: false).Compose(prev: null, screen);
        // 首帧裸差分直出：内容在、无 2026 包裹、无清屏包裹外语义变化
        Assert.Contains("hi", output);
        Assert.DoesNotContain("\x1b[?2026h", output);
        Assert.DoesNotContain("\x1b[?2026l", output);
    }

    [Fact]
    public void Compose_Unsupported_DiffFrame_NoSyncWrapping()
    {
        using var prev = new Screen(4, 1);
        using var next = new Screen(4, 1);
        prev.WriteText(0, 0, "hi");
        next.WriteText(0, 0, "yo");
        var output = new FrameComposer(synchronizedOutputSupported: false).Compose(prev, next);
        Assert.Contains("yo", output);
        Assert.DoesNotContain("\x1b[?2026", output);
    }

    [Fact]
    public void Compose_Unsupported_NoChanges_StillEmptyOutput()
    {
        // 零变化零输出与能力探测正交 — 不支持也不能输出垃圾
        using var prev = new Screen(4, 1);
        using var next = new Screen(4, 1);
        prev.WriteText(0, 0, "hi");
        next.WriteText(0, 0, "hi");
        Assert.Equal(string.Empty, new FrameComposer(synchronizedOutputSupported: false).Compose(prev, next));
    }

    [Fact]
    public void Compose_Supported_KeepsAtomicWrapping()
    {
        using var screen = new Screen(4, 1);
        screen.WriteText(0, 0, "hi");
        var output = new FrameComposer(synchronizedOutputSupported: true).Compose(prev: null, screen);
        Assert.StartsWith(DecPrivateMode.Enable(DecPrivateModeKind.SynchronizedOutput), output);
        Assert.EndsWith(DecPrivateMode.Disable(DecPrivateModeKind.SynchronizedOutput), output);
    }

    [Fact]
    public void Compose_DefaultConstructor_WrapsForBackwardCompat()
    {
        // 默认构造 = 包裹启用（视口模式现行为 — 既有 FrameComposerTests 与 ReplShell 零破坏）
        using var screen = new Screen(4, 1);
        screen.WriteText(0, 0, "hi");
        var output = new FrameComposer().Compose(prev: null, screen);
        Assert.StartsWith(DecPrivateMode.Enable(DecPrivateModeKind.SynchronizedOutput), output);
        Assert.EndsWith(DecPrivateMode.Disable(DecPrivateModeKind.SynchronizedOutput), output);
    }

    [Fact]
    public void Compose_ProbeDriven_WindowsTerminalEnv_Wraps()
    {
        // 探测 → 构造组合链路（生产接线契约预演）：WT_SESSION 支持 → 包裹
        var env = new Dictionary<string, string?> { ["WT_SESSION"] = "uuid" };
        using var screen = new Screen(4, 1);
        screen.WriteText(0, 0, "hi");
        var output = new FrameComposer(TerminalCapabilities.IsSynchronizedOutputSupported(env))
            .Compose(prev: null, screen);
        Assert.StartsWith(DecPrivateMode.Enable(DecPrivateModeKind.SynchronizedOutput), output);
        Assert.EndsWith(DecPrivateMode.Disable(DecPrivateModeKind.SynchronizedOutput), output);
    }

    [Fact]
    public void Compose_ProbeDriven_TmuxEnv_SkipsWrapping()
    {
        // 探测 → 构造组合链路：tmux 不支持 → 裸差分
        var env = new Dictionary<string, string?> { ["TMUX"] = "/tmp/tmux-0/default,1,0" };
        using var screen = new Screen(4, 1);
        screen.WriteText(0, 0, "hi");
        var output = new FrameComposer(TerminalCapabilities.IsSynchronizedOutputSupported(env))
            .Compose(prev: null, screen);
        Assert.Contains("hi", output);
        Assert.DoesNotContain("\x1b[?2026", output);
    }
}
