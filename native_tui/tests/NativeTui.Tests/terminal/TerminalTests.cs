namespace NativeTui.Tests.Terminal;

/// <summary>
/// 帧组装器测试 — 对齐基准 ink 帧输出：DEC 2026 同步输出原子包裹（CellDiffer 注释声明的上层职责）。
/// 无变化零输出；首帧 = 2026h + EraseScreen 全量 + 2026l；增量帧同样被包裹。
/// </summary>
public sealed class FrameComposerTests
{
    [Fact]
    public void Compose_NoChanges_EmptyOutput()
    {
        using var prev = new Screen(4, 1);
        using var next = new Screen(4, 1);
        prev.WriteText(0, 0, "hi");
        next.WriteText(0, 0, "hi");
        Assert.Equal(string.Empty, new FrameComposer().Compose(prev, next));
    }

    [Fact]
    public void Compose_FirstFrame_WrappedInSyncUpdate()
    {
        using var screen = new Screen(4, 1);
        screen.WriteText(0, 0, "hi");
        var output = new FrameComposer().Compose(prev: null, screen);
        Assert.StartsWith(DecPrivateMode.Enable(DecPrivateModeKind.SynchronizedOutput), output);
        Assert.EndsWith(DecPrivateMode.Disable(DecPrivateModeKind.SynchronizedOutput), output);
        Assert.Contains(Csi.EraseScreen(), output);
        Assert.Contains("hi", output);
    }

    [Fact]
    public void Compose_DiffFrame_WrappedInSyncUpdate()
    {
        using var prev = new Screen(4, 1);
        using var next = new Screen(4, 1);
        prev.WriteText(0, 0, "hi");
        next.WriteText(0, 0, "yo");
        var output = new FrameComposer().Compose(prev, next);
        Assert.StartsWith(DecPrivateMode.Enable(DecPrivateModeKind.SynchronizedOutput), output);
        Assert.EndsWith(DecPrivateMode.Disable(DecPrivateModeKind.SynchronizedOutput), output);
        // 差分帧：连续变化段合并输出，无 EraseScreen
        Assert.Contains("yo", output);
        Assert.DoesNotContain(Csi.EraseScreen(), output);
    }

    [Fact]
    public void Compose_Resize_FullRedraw()
    {
        using var prev = new Screen(4, 1);
        using var next = new Screen(8, 1);
        next.WriteText(0, 0, "hi");
        var output = new FrameComposer().Compose(prev, next);
        Assert.Contains(Csi.EraseScreen(), output);
    }
}

/// <summary>Windows 控制台模式位运算测试 — raw 输入模式与 VT 输出模式计算。</summary>
public sealed class ConsoleModeMathTests
{
    [Fact]
    public void ToRawInput_ClearsLineEchoProcessed_KeepsQuickEdit()
    {
        // 典型默认 stdin：PROCESSED|LINE|ECHO|WINDOW|MOUSE|QUICK_EDIT|EXTENDED (0x1F7)
        const uint typical = 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x40 | 0x80;
        var raw = ConsoleModeMath.ToRawInput(typical);
        // 行/回显/处理输入清零；QuickEdit/EXTENDED 保留原样（对齐基准 Node 行为：
        // conhost 宿主下 QuickEdit 是鼠标选择复制的唯一机制，禁用即无法选择）
        Assert.Equal(0x8u | 0x10u | 0x40u | 0x80u, raw);
    }

    [Fact]
    public void ToRawInput_MinimalMode_OnlyClearsRawBits()
    {
        Assert.Equal(0u, ConsoleModeMath.ToRawInput(0x7));
    }

    [Fact]
    public void ToVtOutput_SetsVirtualTerminalProcessing()
    {
        const uint typical = 0x3; // PROCESSED_OUTPUT|WRAP_AT_EOL_OUTPUT
        Assert.Equal(0x3u | 0x4u, ConsoleModeMath.ToVtOutput(typical));
    }
}
