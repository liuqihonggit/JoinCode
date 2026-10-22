namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell ctrl+l 强制重绘集成测试（S6 任务 8）：前缓冲失效 + prevOverride 丢弃 +
/// 空白前置帧触发 InlineComposer FullReset 路径（EraseScreen + 重绘可见区）；
/// 渲染输出通道行为验证（重绘帧输出非空 + 正常恢复增量模式）。
/// </summary>
public sealed class ReplShellIntegrationRepaintTests
{
    /// <summary>构造捕获输出的测试 shell。</summary>
    private static (ReplShell Shell, StringWriter Output) CreateShell()
    {
        var engine = new FakeReplEngine();
        var output = new StringWriter();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            output,
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        return (shell, output);
    }

    private static void CtrlL(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\u000c', ConsoleKey.L, false, false, true));

    /// <summary>ctrl+l — 前缓冲失效（下帧全量）+ prevOverride 丢弃。</summary>
    [Fact]
    public void CtrlL_InvalidatesFront_AndDropsPrevOverride()
    {
        var (shell, output) = CreateShell();
        shell.RenderAndFlush();
        var prevOverrideBefore = shell.PrevOverride;

        CtrlL(shell);

        Assert.Null(shell.PrevOverride);
        Assert.False(shell.BackBuffer is null && shell.FrontBuffer is null);

        // 强制重绘帧 — 输出非空（FullReset 重绘可见区）
        var lengthBefore = output.ToString().Length;
        shell.RenderAndFlush();
        Assert.True(output.ToString().Length > lengthBefore, "强制重绘帧应产生全量输出");
    }

    /// <summary>重绘后恢复正常增量模式 — 无变化帧零输出（缓存基准已重建）。</summary>
    [Fact]
    public void CtrlL_AfterRepaint_IncrementalRenderingResumes()
    {
        var (shell, output) = CreateShell();
        shell.RenderAndFlush();
        CtrlL(shell);
        shell.RenderAndFlush();

        var lengthAfterRepaint = output.ToString().Length;
        shell.RenderAndFlush();

        Assert.Equal(lengthAfterRepaint, output.ToString().Length);
    }
}
