namespace NativeTui.Tests.Terminal;

/// <summary>
/// InlineComposer 测试 — inline（非 alt-screen）渲染语义，对齐基准 ink/log-update.ts：
/// 相对光标操作（CR/LF/CUB/CUF）、增长 append 自然滚入 scrollback、收缩清底部行、
/// scrollback 行不可达/跨尺寸时 fullReset = 2J+3J+光标归位 绝对锚定整帧重绘
/// （对齐基准 clearTerminal.ts:59-69 + renderFrame — reflow 后相对量不可靠）、
/// 零变化零输出。首帧不清屏（保留终端既有历史）。
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
        Assert.DoesNotContain("\x1b[3J", output);
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

    /// <summary>fullReset 绝对锚定（对齐基准 clearTerminal.ts:59-69 — 2J+3J+光标归位；
    /// reflow 后物理光标不可知，相对 CUU 落点失同步 → reset 场景必须绝对锚）。</summary>
    private const string AbsoluteAnchor = "\x1b[2J\x1b[3J\x1b[H";

    /// <summary>变更落在 scrollback 区（不可达）→ fullReset 绝对锚定 + 整帧重绘
    /// （对齐基准 log-update.ts:221-248 scrollback change → fullResetSequence + renderFrame 0..height）。</summary>
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
        Assert.StartsWith(AbsoluteAnchor, output);
        Assert.Contains("AAA", output);
        Assert.Contains("bbb", output);
        Assert.Contains("ccc", output);
    }

    /// <summary>宽度变化 fullReset — 绝对锚定且**整帧重绘**（含视口外 scrollback 行）
    /// （对齐基准 clearTerminal.ts + log-update.ts:509-512 renderFrame 0..height —
    /// 旧实现只重绘底部视口行，banner 等顶部内容在 resize 后不被重发 → 错位无法自愈）。</summary>
    [Fact]
    public void WidthChange_ContentTallerThanViewport_FullResetRedrawsWholeFrame()
    {
        using var prev = new Screen(cols: 10, rows: 30);
        using var next = new Screen(cols: 12, rows: 30);
        prev.WriteText(0, 0, "BANNER");
        prev.WriteText(29, 0, "TAIL");
        next.WriteText(0, 0, "BANNER");
        next.WriteText(29, 0, "TAIL");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(0, 29), frameEnd: new(0, 29), out _);
        Assert.StartsWith(AbsoluteAnchor, output);
        Assert.Contains("BANNER", output);
        Assert.Contains("TAIL", output);
    }

    [Fact]
    public void WidthChange_TriggersFullReset()
    {
        using var prev = new Screen(cols: 10, rows: 1);
        using var next = new Screen(cols: 12, rows: 1);
        prev.WriteText(0, 0, "hi");
        next.WriteText(0, 0, "hi");
        var output = Compose(prev, next, viewportRows: 10, prevCursor: new(0, 0), frameEnd: new(0, 0), out _);
        Assert.StartsWith(AbsoluteAnchor, output);
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
    /// 从视口顶清行会错擦可见区并引发多余滚屏，后续帧物理失同步）→ 必须走 fullReset 绝对锚定
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

        Assert.StartsWith(AbsoluteAnchor, output);
        Assert.Contains("keep0", output);
        Assert.Contains("keep4", output);
    }
}
