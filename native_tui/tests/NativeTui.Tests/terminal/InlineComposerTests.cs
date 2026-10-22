namespace NativeTui.Tests.Terminal;

/// <summary>
/// InlineComposer 测试 — inline（非 alt-screen）渲染语义，对齐基准 ink/log-update.ts：
/// 相对光标操作（CR/LF/CUB/CUF）、增长 append 自然滚入 scrollback、收缩清底部行、
/// scrollback 行不可达时 fullReset、零变化零输出。首帧不清屏（保留终端既有历史）。
/// </summary>
public sealed class InlineComposerTests
{
    private static string Compose(Screen? prev, Screen next, int viewportRows, CursorPos prevCursor, CursorPos frameEnd, out CursorPos nextCursor) =>
        new InlineComposer().Compose(prev, next, viewportRows, prevCursor, frameEnd, out nextCursor);

    [Fact]
    public void FirstFrame_WritesRowsWithoutClearingScreen()
    {
        using var next = new Screen(cols: 10, rows: 2);
        next.WriteText(0, 0, "hi");
        next.WriteText(1, 0, "yo");
        var output = Compose(null, next, viewportRows: 10, prevCursor: new(0, 0), frameEnd: new(0, 1), out _);
        Assert.DoesNotContain("\x1b[2J", output);
        Assert.StartsWith("\rhi", output);
        Assert.Contains("\r\nyo", output);
    }

    [Fact]
    public void SameSizeChange_EmitsSegmentWithRelativeMoves()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 10, rows: 1);
        prev.WriteText(0, 0, "abcd");
        next.WriteText(0, 0, "abXd");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(0, 0), frameEnd: new(0, 0), out _);
        Assert.Equal("\x1b[2CX\x1b[3D", output);
    }

    [Fact]
    public void SameSizeChange_CursorLeftOfSegment_MovesBackward()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 10, rows: 1);
        prev.WriteText(0, 0, "abcd");
        next.WriteText(0, 0, "abXd");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(5, 0), frameEnd: new(0, 0), out _);
        Assert.StartsWith("\x1b[3D", output);
        Assert.Contains("X", output);
    }

    [Fact]
    public void GrowthRow_AppendedWithNewlineAdvance()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 10, rows: 2);
        prev.WriteText(0, 0, "hi");
        next.WriteText(0, 0, "hi");
        next.WriteText(1, 0, "new");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(0, 0), frameEnd: new(0, 1), out _);
        Assert.DoesNotContain("\x1b[2J", output);
        Assert.Contains("\r\nnew", output);
        Assert.DoesNotContain("hi", output);
    }

    [Fact]
    public void Shrink_ClearsBottomRows()
    {
        using var prev = new Screen(cols: 10, rows: 2);
        using var next = new Screen(cols: 10, rows: 1);
        prev.WriteText(0, 0, "hi");
        prev.WriteText(1, 0, "old");
        next.WriteText(0, 0, "hi");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(0, 1), frameEnd: new(0, 0), out _);
        Assert.DoesNotContain("\x1b[2J", output);
        Assert.Contains("\x1b[2K", output);
        Assert.DoesNotContain("hi", output);
        Assert.DoesNotContain("old", output);
    }

    [Fact]
    public void ChangeInScrollback_TriggersFullReset()
    {
        using var prev = new Screen(cols: 10, rows: 3);
        using var next = new Screen(cols: 10, rows: 3);
        prev.WriteText(0, 0, "aaa");
        prev.WriteText(1, 0, "bbb");
        prev.WriteText(2, 0, "ccc");
        next.WriteText(0, 0, "AAA");
        next.WriteText(1, 0, "bbb");
        next.WriteText(2, 0, "ccc");
        var output = Compose(prev, next, viewportRows: 2, prevCursor: new(0, 2), frameEnd: new(0, 2), out _);
        Assert.Contains("\x1b[2J", output);
        Assert.Contains("bbb", output);
        Assert.Contains("ccc", output);
        Assert.DoesNotContain("AAA", output);
        Assert.DoesNotContain("aaa", output);
    }

    [Fact]
    public void WidthChange_TriggersFullReset()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 12, rows: 1);
        prev.WriteText(0, 0, "hi");
        next.WriteText(0, 0, "hi");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(0, 0), frameEnd: new(0, 0), out _);
        Assert.Contains("\x1b[2J", output);
        Assert.Contains("hi", output);
    }

    [Fact]
    public void NoChange_NoCursorMove_EmptyOutput()
    {
        using var prev = new Screen(cols: 10, rows: 2);
        using var next = new Screen(cols: 10, rows: 2);
        prev.WriteText(0, 0, "same");
        next.WriteText(0, 0, "same");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(3, 1), frameEnd: new(3, 1), out _);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void NoContentChange_CursorMovesOnly_EmitsRelativeMove()
    {
        using var prev = new Screen(cols: 10, rows: 2);
        using var next = new Screen(cols: 10, rows: 2);
        prev.WriteText(0, 0, "same");
        next.WriteText(0, 0, "same");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(3, 1), frameEnd: new(1, 0), out var nextCursor);
        Assert.Equal("\r\x1b[A\x1b[C", output);
        Assert.Equal(new CursorPos(1, 0), nextCursor);
    }

    /// <summary>收缩清除超出视口可达范围（被清首行在 scrollback 区 — CUU 被终端钳制在视口顶，
    /// 从视口顶清行会错擦可见区并引发多余滚屏，后续帧物理失同步）→ 必须走 fullReset
    /// （对齐基准 log-update.ts:259-271 shrink 守卫：linesToClear &gt; viewport.height → fullReset）。</summary>
    [Fact]
    public void ShrinkBeyondViewport_TriggersFullReset()
    {
        using var prev = new Screen(cols: 10, rows: 30);
        using var next = new Screen(cols: 10, rows: 5);
        for (var row = 0; row < 5; row++)
        {
            prev.WriteText(row, 0, "keep" + row);
            next.WriteText(row, 0, "keep" + row);
        }

        // prev 30 行 / viewport 10 → 20 行在 scrollback；收缩到 5 行 → 被清 25 行 > 视口 10
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(0, 29), frameEnd: new(0, 4), out _);

        Assert.Contains("\x1b[2J", output);
        Assert.Contains("keep0", output);
    }
}
