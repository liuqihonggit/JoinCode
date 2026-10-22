namespace NativeTui.Tests.Terminal;

/// <summary>
/// 光标显隐生命周期测试 — 对齐基准 ink/termio/dec.ts HIDE_CURSOR（\x1b[?25l）/
/// SHOW_CURSOR（\x1b[?25h）：运行期隐藏、退出恢复的序列常量与发射方法契约
/// （ReplShell 接线为未决问题 — 此处锁序列本身）。
/// </summary>
public sealed class TerminalCursorLifecycleTests
{
    [Fact]
    public void Hide_IsDec25Reset()
    {
        Assert.Equal("\x1b[?25l", CursorLifecycle.Hide);
        Assert.Equal(DecPrivateMode.Disable(DecPrivateModeKind.CursorVisibility), CursorLifecycle.Hide);
    }

    [Fact]
    public void Show_IsDec25Set()
    {
        Assert.Equal("\x1b[?25h", CursorLifecycle.Show);
        Assert.Equal(DecPrivateMode.Enable(DecPrivateModeKind.CursorVisibility), CursorLifecycle.Show);
    }

    [Fact]
    public void HideAndShow_AreComplementaryPair()
    {
        // 同一 DEC 模式（25）的 set/reset 互补对 — 隐藏后必须能用 Show 恢复
        Assert.NotEqual(CursorLifecycle.Hide, CursorLifecycle.Show);
        Assert.StartsWith("\x1b[?25", CursorLifecycle.Hide);
        Assert.StartsWith("\x1b[?25", CursorLifecycle.Show);
        Assert.EndsWith("l", CursorLifecycle.Hide);
        Assert.EndsWith("h", CursorLifecycle.Show);
    }

    [Fact]
    public void HideCursor_WritesSequenceToOutput()
    {
        using var output = new StringWriter();
        CursorLifecycle.HideCursor(output);
        Assert.Equal("\x1b[?25l", output.ToString());
    }

    [Fact]
    public void ShowCursor_WritesSequenceToOutput()
    {
        using var output = new StringWriter();
        CursorLifecycle.ShowCursor(output);
        Assert.Equal("\x1b[?25h", output.ToString());
    }

    [Fact]
    public void HideThenShow_RoundTripRestoresVisibleCursor()
    {
        // 退出恢复语义：运行期隐藏 → 退出恢复，末态硬件光标可见
        using var output = new StringWriter();
        CursorLifecycle.HideCursor(output);
        CursorLifecycle.ShowCursor(output);
        Assert.Equal("\x1b[?25l\x1b[?25h", output.ToString());
    }

    [Fact]
    public void ShowCursor_IdempotentReplay()
    {
        // 崩溃兜底路径可重入：重复发射 Show 幂等无副作用（纯序列追加）
        using var output = new StringWriter();
        CursorLifecycle.ShowCursor(output);
        CursorLifecycle.ShowCursor(output);
        Assert.Equal("\x1b[?25h\x1b[?25h", output.ToString());
    }
}
