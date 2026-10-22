namespace NativeTui.Tests.Terminal;

/// <summary>
/// 崩溃/退出路径终端恢复兜底测试 — CursorLifecycle.cs 注释契约兑现（"宿主全局异常兜底统一补发 Show"）：
/// 钩子触发时同步发射硬件光标 Show + bracketed paste Disable（幂等可重入），写失败静默（兜底不二抛）。
/// </summary>
public sealed class TerminalRestoreGuardTests
{
    /// <summary>钩子触发 — 发射 Show + bracketed paste Disable（对齐基准 ink.tsx:1472-1505 signal-exit 恢复链）。</summary>
    [Fact]
    public void Restore_EmitsCursorShow_AndBracketedPasteDisable()
    {
        var output = new StringWriter();
        using var guard = new TerminalRestoreGuard(output);

        guard.RestoreTerminalForTest();

        var text = output.ToString();
        Assert.Contains(CursorLifecycle.Show, text);
        Assert.Contains(DecPrivateMode.Disable(DecPrivateModeKind.BracketedPaste), text);
    }

    /// <summary>重复触发幂等 — 恢复序列可安全重入（多钩子同时触达不产生破坏）。</summary>
    [Fact]
    public void Restore_IsIdempotent_AcrossMultipleTriggers()
    {
        var output = new StringWriter();
        using var guard = new TerminalRestoreGuard(output);

        guard.RestoreTerminalForTest();
        guard.RestoreTerminalForTest();

        Assert.Contains(CursorLifecycle.Show, output.ToString());
    }

    /// <summary>写失败静默 — 兜底路径不因输出管道已死而二次抛出（崩溃场景输出可能已失效）。</summary>
    [Fact]
    public void Restore_SwallowsWriteFailure()
    {
        using var deadPipe = new DeadWriter();
        using var guard = new TerminalRestoreGuard(deadPipe);

        guard.RestoreTerminalForTest();
    }

    /// <summary>已 Dispose 后不再发射（正常退出路径由 WriteExit 对称恢复，guard 解除钩子）。</summary>
    [Fact]
    public void AfterDispose_NoFurtherEmission()
    {
        var output = new StringWriter();
        var guard = new TerminalRestoreGuard(output);
        guard.Dispose();

        guard.RestoreTerminalForTest();

        Assert.Equal(string.Empty, output.ToString());
    }

    /// <summary>写入即抛的测试管道（模拟崩溃场景下已失效的 stdout）。</summary>
    private sealed class DeadWriter : StringWriter
    {
        public override void Write(char value) => throw new IOException("pipe dead");

        public override void Write(string? value) => throw new IOException("pipe dead");

        public override void Write(ReadOnlySpan<char> buffer) => throw new IOException("pipe dead");

        public override void Flush() => throw new IOException("pipe dead");
    }
}
