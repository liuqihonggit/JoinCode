namespace NativeTui.Tests.Diff;

/// <summary>
/// CellDiffer 测试 — 前后帧逐 cell diff，只输出变化部分（对齐基准 log-update.ts）。
/// 光标用绝对定位（CursorPosition），alt-screen 全帧可达无需 scrollback 全量重置路径。
/// </summary>
public sealed class CellDifferTests
{
    [Fact]
    public void FirstFrame_ClearsScreenThenWritesContent()
    {
        using var next = new Screen(cols: 10, rows: 2);
        next.WriteText(0, 0, "hi");
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev: null, next);
        Assert.StartsWith("\x1b[2J", output);
        Assert.Contains("\x1b[H", output);
        Assert.Contains("hi", output);
    }

    [Fact]
    public void FirstFrame_BlankRow_Skipped()
    {
        using var next = new Screen(cols: 10, rows: 3);
        next.WriteText(2, 0, "end");
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev: null, next);
        // 空行 0/1 不输出内容，只定位到行 2
        Assert.DoesNotContain("\x1b[1;1H", output);
        Assert.DoesNotContain("\x1b[2;1H", output);
        Assert.Contains("\x1b[3;1H", output);
        Assert.Contains("end", output);
    }

    [Fact]
    public void NoChange_EmptyOutput()
    {
        using var prev = new Screen(cols: 10, rows: 2);
        using var next = new Screen(cols: 10, rows: 2);
        prev.WriteText(0, 0, "same");
        next.WriteText(0, 0, "same");
        var diff = new CellDiffer();
        Assert.Equal(string.Empty, diff.RenderFrame(prev, next));
    }

    [Fact]
    public void SingleCellChange_OnlyThatCellEmitted()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 10, rows: 1);
        prev.WriteText(0, 0, "abcd");
        next.WriteText(0, 0, "abXd");
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev, next);
        Assert.Equal("\x1b[1;3HX", output);
    }

    [Fact]
    public void ContiguousChange_EmittedAsOneRun()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 10, rows: 1);
        prev.WriteText(0, 2, "abcd");
        next.WriteText(0, 2, "XYcd");
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev, next);
        Assert.Equal("\x1b[1;3HXY", output);
    }

    [Fact]
    public void StyleChange_EmitsTransitionThenChar()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 10, rows: 1);
        prev.WriteText(0, 0, "ab");
        var orange = next.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(215, 119, 87) });
        next.WriteText(0, 0, "ab", orange);
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev, next);
        Assert.Equal("\x1b[H\x1b[38;2;215;119;87mab\x1b[39m", output);
    }

    [Fact]
    public void MultiRowChange_EachRowPositioned()
    {
        using var prev = new Screen(cols: 10, rows: 3);
        using var next = new Screen(cols: 10, rows: 3);
        prev.WriteText(0, 0, "aaa");
        prev.WriteText(2, 0, "ccc");
        next.WriteText(0, 0, "aXa");
        next.WriteText(2, 0, "cXc");
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev, next);
        Assert.Equal("\x1b[1;2HX\x1b[3;2HX", output);
    }

    [Fact]
    public void DeletedContent_EmitsSpaces()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 10, rows: 1);
        prev.WriteText(0, 0, "abcd");
        next.WriteText(0, 0, "ab");
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev, next);
        Assert.Equal("\x1b[1;3H  ", output);
    }

    [Fact]
    public void CjkWideCharChange_SingleContiguousSegmentWithWideAdvance()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 10, rows: 1);
        prev.WriteText(0, 0, "中");
        next.WriteText(0, 2, "中");
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev, next);
        // 擦除两格+新宽字符合并为一个连续段：双空格后打印"中"，终端自动推进光标两格
        Assert.Equal("\x1b[H  中", output);
    }

    [Fact]
    public void SizeChange_FullRedraw()
    {
        using var prev = new Screen(cols: 10, rows: 2);
        using var next = new Screen(cols: 8, rows: 4);
        next.WriteText(0, 0, "resized");
        var diff = new CellDiffer();
        var output = diff.RenderFrame(prev, next);
        Assert.StartsWith("\x1b[2J", output);
        Assert.Contains("resized", output);
    }
}

/// <summary>
/// ThrottleGate 测试 — 60fps 渲染节流（对齐基准 throttle(fn, 16ms, leading+trailing)）。
/// </summary>
public sealed class ThrottleGateTests
{
    [Fact]
    public void FirstCall_LeadingEdge_Passes()
    {
        var gate = new ThrottleGate(intervalMs: 16);
        Assert.True(gate.TryEnter(TimeSpan.FromMilliseconds(0)));
    }

    [Fact]
    public void CallWithinInterval_Blocked()
    {
        var gate = new ThrottleGate(intervalMs: 16);
        gate.TryEnter(TimeSpan.FromMilliseconds(0));
        Assert.False(gate.TryEnter(TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void CallAfterInterval_Passes()
    {
        var gate = new ThrottleGate(intervalMs: 16);
        gate.TryEnter(TimeSpan.FromMilliseconds(0));
        Assert.True(gate.TryEnter(TimeSpan.FromMilliseconds(16)));
    }

    [Fact]
    public void CallWithinInterval_SignalsTrailingPending()
    {
        // trailing: 间隔内被拒 → 标记 pending，到点后应补一次
        var gate = new ThrottleGate(intervalMs: 16);
        gate.TryEnter(TimeSpan.FromMilliseconds(0));
        gate.TryEnter(TimeSpan.FromMilliseconds(10));
        Assert.True(gate.HasPending);
        // 到达 trailing 窗口后放行
        Assert.True(gate.TryEnter(TimeSpan.FromMilliseconds(20)));
        Assert.False(gate.HasPending);
    }

    [Fact]
    public void NoBlockedCall_NoTrailingPending()
    {
        var gate = new ThrottleGate(intervalMs: 16);
        gate.TryEnter(TimeSpan.FromMilliseconds(0));
        Assert.False(gate.HasPending);
    }
}
