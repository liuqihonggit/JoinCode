namespace NativeTui.Tests.Repl.Selection;

/// <summary>
/// SelectionState 选区状态机测试 — 对齐基准 ink/selection.ts + App.tsx 多击判定（92-93/542-644）
/// + ink.tsx 路由（1328-1357）：单击拖拽流式提取（跨行）、双击词 run（三分类字符类）、
/// 三击整行、多击后按词/行扩展（前向/反向/重叠三态）、shift+click 锚点不动扩展、
/// 释放固化保留 + 新 Press 重锚定、时间窗/距离窗/断链/续链、空行与越界兜底、
/// GetSelectedText 多行拼接 + 逐行尾空白裁剪。
/// 时钟经构造注入（测试可控时间戳推进时间窗），屏幕经行文本委托投影。
/// </summary>
public sealed class SelectionStateTests
{
    /// <summary>构造屏幕投影（定长行文本数组；越界行返回空）。</summary>
    private static SelectionScreen ScreenOf(int cols, params string[] lines) =>
        new(row => row >= 0 && row < lines.Length ? lines[row].AsSpan() : ReadOnlySpan<char>.Empty, cols, lines.Length);

    /// <summary>构造左键 Press 事件。</summary>
    private static MouseEvent LeftPress(int col, int row, bool shift = false) =>
        new(MouseKind.Press, MouseButton.Left, col, row, shift, false, false);

    /// <summary>构造左键 Drag 事件。</summary>
    private static MouseEvent LeftDrag(int col, int row) =>
        new(MouseKind.Drag, MouseButton.Left, col, row, false, false, false);

    /// <summary>构造左键 Release 事件。</summary>
    private static MouseEvent LeftRelease(int col, int row) =>
        new(MouseKind.Release, MouseButton.Left, col, row, false, false, false);

    /// <summary>单击+拖拽字符选区 — 跨行流式提取（首行起列裁剪 + 尾行止列 + '\n' join — selection.ts:784-788）。</summary>
    [Fact]
    public void ClickAndDrag_CrossRowFlowsExtraction()
    {
        var screen = ScreenOf(20, "hello world", "bye moon", "xyz");
        var state = new SelectionState();

        state.HandleMouse(LeftPress(2, 0), screen);
        state.HandleMouse(LeftDrag(5, 0), screen);
        state.HandleMouse(LeftDrag(1, 1), screen);
        state.HandleMouse(LeftRelease(1, 1), screen);

        Assert.True(state.HasSelection);
        Assert.Equal(new SelectionBounds(new SelectionPoint(2, 0), new SelectionPoint(1, 1)), state.Bounds!.Value);
        Assert.Equal("llo world\nby", state.GetSelectedText(screen));
        Assert.True(state.IsSelected(0, 2));
        Assert.False(state.IsSelected(0, 1));
        Assert.True(state.IsSelected(0, 19));
        Assert.True(state.IsSelected(1, 0));
        Assert.True(state.IsSelected(1, 1));
        Assert.False(state.IsSelected(1, 2));
        Assert.False(state.IsSelected(2, 0));
    }

    /// <summary>单击无拖动 — 无选区（focus 延迟到首次拖动 — selection.ts:86-88：裸单击不产生高亮）。</summary>
    [Fact]
    public void SingleClickWithoutDrag_NoSelection()
    {
        var screen = ScreenOf(20, "hello world");
        var state = new SelectionState();

        state.HandleMouse(LeftPress(3, 0), screen);
        state.HandleMouse(LeftRelease(3, 0), screen);

        Assert.False(state.HasSelection);
        Assert.Null(state.Bounds);
        Assert.False(state.IsSelected(0, 3));
        Assert.False(state.IsSelecting);
    }

    /// <summary>双击词选区 — 同类字符 run 扩展（selection.ts:240-254）；Release 后选区保留（selection.ts:116-120）。</summary>
    [Fact]
    public void DoubleClick_SelectsWordRun()
    {
        var screen = ScreenOf(20, "hello world foo");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(6, 0), screen);
        time = 100;
        state.HandleMouse(LeftPress(6, 0), screen);
        state.HandleMouse(LeftRelease(6, 0), screen);

        Assert.Equal(SelectionGranularity.Word, state.Granularity);
        Assert.True(state.HasSelection);
        Assert.Equal(new SelectionBounds(new SelectionPoint(6, 0), new SelectionPoint(10, 0)), state.Bounds!.Value);
        Assert.Equal("world", state.GetSelectedText(screen));
        Assert.True(state.IsSelected(0, 6));
        Assert.False(state.IsSelected(0, 5));
        Assert.True(state.IsSelected(0, 10));
        Assert.False(state.IsSelected(0, 11));
    }

    /// <summary>时间窗外两次单击 — 断链重锚（App.tsx:598：500ms 窗口外 = 新链 → 字符模式）。</summary>
    [Fact]
    public void ClickOutsideTimeWindow_StartsFreshChain()
    {
        var screen = ScreenOf(20, "hello world");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(6, 0), screen);
        time = 600;
        state.HandleMouse(LeftPress(6, 0), screen);

        Assert.Equal(SelectionGranularity.Character, state.Granularity);
        Assert.False(state.HasSelection);
    }

    /// <summary>距离窗外两次单击 — 断链重锚（App.tsx:598：|Δcol| &gt; 1 cell 容差 = 新链）。</summary>
    [Fact]
    public void ClickOutsideDistanceWindow_StartsFreshChain()
    {
        var screen = ScreenOf(20, "hello world");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(4, 0), screen);
        state.HandleMouse(LeftPress(6, 0), screen);

        Assert.Equal(SelectionGranularity.Character, state.Granularity);
        Assert.False(state.HasSelection);
    }

    /// <summary>三击行选区 — 整行 [0, Cols-1]（selection.ts:368-380）+ 行内 IsSelected 边界。</summary>
    [Fact]
    public void TripleClick_SelectsFullLine()
    {
        var screen = ScreenOf(20, "hello world", "bye moon", "xyz");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(3, 1), screen);
        state.HandleMouse(LeftPress(3, 1), screen);
        state.HandleMouse(LeftPress(3, 1), screen);

        Assert.Equal(SelectionGranularity.Line, state.Granularity);
        Assert.Equal(new SelectionBounds(new SelectionPoint(0, 1), new SelectionPoint(19, 1)), state.Bounds!.Value);
        Assert.Equal("bye moon", state.GetSelectedText(screen));
        Assert.True(state.IsSelected(1, 0));
        Assert.True(state.IsSelected(1, 19));
        Assert.False(state.IsSelected(0, 0));
        Assert.False(state.IsSelected(2, 0));
    }

    /// <summary>双击后拖拽 — 词粒度三态扩展：前向（跨过锚定词）/ 反向（回拉）/ 重叠（恰选锚定词 — selection.ts:408-420）。</summary>
    [Fact]
    public void DoubleClickDrag_ExtendsForwardBackwardOverlap()
    {
        var screen = ScreenOf(20, "alpha beta gamma");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(7, 0), screen);
        state.HandleMouse(LeftPress(7, 0), screen);
        Assert.Equal("beta", state.GetSelectedText(screen));

        state.HandleMouse(LeftDrag(13, 0), screen);
        Assert.Equal(new SelectionBounds(new SelectionPoint(6, 0), new SelectionPoint(15, 0)), state.Bounds!.Value);
        Assert.Equal("beta gamma", state.GetSelectedText(screen));

        state.HandleMouse(LeftDrag(1, 0), screen);
        Assert.Equal(new SelectionBounds(new SelectionPoint(0, 0), new SelectionPoint(9, 0)), state.Bounds!.Value);
        Assert.Equal("alpha beta", state.GetSelectedText(screen));

        state.HandleMouse(LeftDrag(8, 0), screen);
        Assert.Equal(new SelectionBounds(new SelectionPoint(6, 0), new SelectionPoint(9, 0)), state.Bounds!.Value);
        Assert.Equal("beta", state.GetSelectedText(screen));
    }

    /// <summary>双击后拖拽跨行 — 词扩展流式衔接（行序比较 — selection.ts:226-231）。</summary>
    [Fact]
    public void DoubleClickDrag_ExtendsAcrossRows()
    {
        var screen = ScreenOf(20, "alpha beta gamma", "delta omega");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(7, 0), screen);
        state.HandleMouse(LeftPress(7, 0), screen);
        state.HandleMouse(LeftDrag(2, 1), screen);

        Assert.Equal(new SelectionBounds(new SelectionPoint(6, 0), new SelectionPoint(4, 1)), state.Bounds!.Value);
        Assert.Equal("beta gamma\ndelta", state.GetSelectedText(screen));
    }

    /// <summary>三击后拖拽 — 行粒度扩展到目标行（整行范围，selection.ts:404-406）。</summary>
    [Fact]
    public void TripleClickDrag_ExtendsByLine()
    {
        var screen = ScreenOf(20, "alpha beta gamma", "delta omega", "xyz");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(2, 0), screen);
        state.HandleMouse(LeftPress(2, 0), screen);
        state.HandleMouse(LeftPress(2, 0), screen);
        state.HandleMouse(LeftDrag(2, 2), screen);

        Assert.Equal(new SelectionBounds(new SelectionPoint(0, 0), new SelectionPoint(19, 2)), state.Bounds!.Value);
        Assert.Equal("alpha beta gamma\ndelta omega\nxyz", state.GetSelectedText(screen));
    }

    /// <summary>shift+click 扩展 — 锚点不动只改活动点（moveFocus 语义 — selection.ts:442-450）。</summary>
    [Fact]
    public void ShiftClick_ExtendsAnchorFixed()
    {
        var screen = ScreenOf(20, "hello world");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(2, 0), screen);
        state.HandleMouse(LeftDrag(5, 0), screen);
        state.HandleMouse(LeftRelease(5, 0), screen);
        Assert.Equal(new SelectionBounds(new SelectionPoint(2, 0), new SelectionPoint(5, 0)), state.Bounds!.Value);

        time = 100;
        state.HandleMouse(LeftPress(9, 0, shift: true), screen);

        Assert.Equal(new SelectionPoint(2, 0), state.Anchor);
        Assert.Equal(new SelectionBounds(new SelectionPoint(2, 0), new SelectionPoint(9, 0)), state.Bounds!.Value);
        Assert.Equal(SelectionGranularity.Character, state.Granularity);
        Assert.Equal("llo worl", state.GetSelectedText(screen));
    }

    /// <summary>词选后 shift+click — 降级字符粒度（moveFocus 清 anchorSpan — selection.ts:444）。</summary>
    [Fact]
    public void ShiftClickAfterWordSelection_DropsToCharacter()
    {
        var screen = ScreenOf(20, "alpha beta gamma");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(7, 0), screen);
        state.HandleMouse(LeftPress(7, 0), screen);
        Assert.Equal(SelectionGranularity.Word, state.Granularity);

        time = 100;
        state.HandleMouse(LeftPress(0, 0, shift: true), screen);

        Assert.Equal(SelectionGranularity.Character, state.Granularity);
        Assert.Equal(new SelectionBounds(new SelectionPoint(0, 0), new SelectionPoint(6, 0)), state.Bounds!.Value);
        Assert.Equal("alpha b", state.GetSelectedText(screen));
    }

    /// <summary>shift+click 无既有锚点 — 退化为普通锚定（终端惯例：无选区时等同单击）。</summary>
    [Fact]
    public void ShiftClickWithNoAnchor_StartsFresh()
    {
        var screen = ScreenOf(20, "hello world");
        var state = new SelectionState();

        state.HandleMouse(LeftPress(1, 0, shift: true), screen);
        Assert.False(state.HasSelection);

        state.HandleMouse(LeftDrag(3, 0), screen);
        Assert.Equal(new SelectionBounds(new SelectionPoint(1, 0), new SelectionPoint(3, 0)), state.Bounds!.Value);
    }

    /// <summary>释放后选区保留、新 Press 重锚定（startSelection 重置 — selection.ts:79-98）+ 粒度复位。</summary>
    [Fact]
    public void ReleaseKeepsSelection_NewPressReanchors()
    {
        var screen = ScreenOf(20, "hello world", "bye moon");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(6, 0), screen);
        state.HandleMouse(LeftPress(6, 0), screen);
        state.HandleMouse(LeftRelease(6, 0), screen);
        Assert.True(state.HasSelection);
        Assert.True(state.IsSelected(0, 6));
        Assert.Equal(SelectionGranularity.Word, state.Granularity);

        time = 1000;
        state.HandleMouse(LeftPress(1, 1), screen);
        Assert.False(state.HasSelection);
        Assert.Equal(SelectionGranularity.Character, state.Granularity);
        Assert.False(state.IsSelected(0, 6));

        state.HandleMouse(LeftDrag(2, 1), screen);
        Assert.Equal(new SelectionBounds(new SelectionPoint(1, 1), new SelectionPoint(2, 1)), state.Bounds!.Value);
    }

    /// <summary>Clear（Esc）— 全清（selection.ts:122-134）。</summary>
    [Fact]
    public void Clear_ClearsAllState()
    {
        var screen = ScreenOf(20, "hello world");
        var state = new SelectionState();

        state.HandleMouse(LeftPress(2, 0), screen);
        state.HandleMouse(LeftDrag(5, 0), screen);
        Assert.True(state.HasSelection);

        state.Clear();

        Assert.False(state.HasSelection);
        Assert.Null(state.Bounds);
        Assert.False(state.IsSelecting);
        Assert.False(state.IsSelected(0, 3));
        Assert.Equal(SelectionGranularity.Character, state.Granularity);
    }

    /// <summary>GetSelectedText 多行提取 + 逐行尾空白裁剪（中间空白保留 — selection.ts:743 的 \s+$ 语义）。</summary>
    [Fact]
    public void GetSelectedText_TrimsTrailingWhitespacePerRow()
    {
        var screen = ScreenOf(15, "abc   def   ", "  xy  ");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(0, 0), screen);
        state.HandleMouse(LeftPress(0, 0), screen);
        state.HandleMouse(LeftPress(0, 0), screen);
        state.HandleMouse(LeftDrag(0, 1), screen);

        Assert.Equal("abc   def\n  xy", state.GetSelectedText(screen));
    }

    /// <summary>反向拖拽 — 边界归一化（focus 在 anchor 之前 — selection.ts:689-691）。</summary>
    [Fact]
    public void ReverseDrag_NormalizesBounds()
    {
        var screen = ScreenOf(20, "hello world");
        var state = new SelectionState();

        state.HandleMouse(LeftPress(5, 0), screen);
        state.HandleMouse(LeftDrag(1, 0), screen);

        Assert.Equal(new SelectionBounds(new SelectionPoint(1, 0), new SelectionPoint(5, 0)), state.Bounds!.Value);
        Assert.Equal("ello", state.GetSelectedText(screen));
        Assert.True(state.IsSelected(0, 1));
        Assert.True(state.IsSelected(0, 5));
        Assert.False(state.IsSelected(0, 0));
        Assert.False(state.IsSelected(0, 6));
    }

    /// <summary>Drag 首次 motion 落在锚点同 cell — no-op 防抖动（selection.ts:111-113）；focus 置位后回锚点正常跟踪。</summary>
    [Fact]
    public void DragFirstMotionAtAnchor_NoOpThenTracks()
    {
        var screen = ScreenOf(20, "hello world");
        var state = new SelectionState();

        state.HandleMouse(LeftPress(3, 0), screen);
        state.HandleMouse(LeftDrag(3, 0), screen);
        Assert.False(state.HasSelection);

        state.HandleMouse(LeftDrag(4, 0), screen);
        Assert.Equal(new SelectionBounds(new SelectionPoint(3, 0), new SelectionPoint(4, 0)), state.Bounds!.Value);

        // focus 已置位后回到锚点正常跟踪 — 选区收缩为 1-cell（selection.ts:110 "including back to anchor"）
        state.HandleMouse(LeftDrag(3, 0), screen);
        Assert.Equal(new SelectionBounds(new SelectionPoint(3, 0), new SelectionPoint(3, 0)), state.Bounds!.Value);
        Assert.Equal("l", state.GetSelectedText(screen));
    }

    /// <summary>非左键 Press — 断多击链（App.tsx:572-575：右键 Press 后窗内再击为单击）。</summary>
    [Fact]
    public void NonLeftPress_BreaksMultiClickChain()
    {
        var screen = ScreenOf(20, "hello world");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(4, 0), screen);
        state.HandleMouse(new MouseEvent(MouseKind.Press, MouseButton.Right, 4, 0, false, false, false), screen);
        state.HandleMouse(LeftPress(4, 0), screen);

        Assert.Equal(SelectionGranularity.Character, state.Granularity);
        Assert.False(state.HasSelection);
    }

    /// <summary>Release 不断多击链（NSEvent clickCount 语义 — App.tsx:638-644）+ 四击 cap 在行粒度（App.tsx:610-611）。</summary>
    [Fact]
    public void ReleaseDoesNotBreakMultiClickChain()
    {
        var screen = ScreenOf(20, "hello world");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(6, 0), screen);
        state.HandleMouse(LeftPress(6, 0), screen);
        Assert.Equal(SelectionGranularity.Word, state.Granularity);

        state.HandleMouse(LeftRelease(6, 0), screen);
        state.HandleMouse(LeftPress(6, 0), screen);
        Assert.Equal(SelectionGranularity.Line, state.Granularity);

        state.HandleMouse(LeftRelease(6, 0), screen);
        state.HandleMouse(LeftPress(6, 0), screen);
        Assert.Equal(SelectionGranularity.Line, state.Granularity);
    }

    /// <summary>双击标点 run — 类 2（非词字符）同类扩展（selection.ts:144-150：双击标点选标点 run）。</summary>
    [Fact]
    public void DoubleClickPunctuation_SelectsPunctuationRun()
    {
        var screen = ScreenOf(12, "foo ;: bar");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(5, 0), screen);
        state.HandleMouse(LeftPress(5, 0), screen);

        Assert.Equal(new SelectionBounds(new SelectionPoint(4, 0), new SelectionPoint(5, 0)), state.Bounds!.Value);
        Assert.Equal(";:", state.GetSelectedText(screen));
        Assert.False(state.IsSelected(0, 3));
        Assert.True(state.IsSelected(0, 4));
        Assert.True(state.IsSelected(0, 5));
        Assert.False(state.IsSelected(0, 6));
    }

    /// <summary>双击路径词 — iTerm2 词内标点集（_ / . - + ~ \，selection.ts:142）连通整段路径。</summary>
    [Fact]
    public void DoubleClickWord_IncludesPathSeparators()
    {
        var screen = ScreenOf(24, "see /usr/bin/bash now");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(5, 0), screen);
        state.HandleMouse(LeftPress(5, 0), screen);

        Assert.Equal(new SelectionBounds(new SelectionPoint(4, 0), new SelectionPoint(16, 0)), state.Bounds!.Value);
        Assert.Equal("/usr/bin/bash", state.GetSelectedText(screen));
    }

    /// <summary>双击空行（行文本不存在）— 词扫描无果兜底 focus=anchor 单 cell（ink.tsx:1338）。</summary>
    [Fact]
    public void MultiClickOnEmptyRow_FallsBackToPoint()
    {
        var screen = new SelectionScreen(_ => ReadOnlySpan<char>.Empty, 10, 5);
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(3, 2), screen);
        state.HandleMouse(LeftPress(3, 2), screen);

        Assert.True(state.HasSelection);
        Assert.Equal(new SelectionBounds(new SelectionPoint(3, 2), new SelectionPoint(3, 2)), state.Bounds!.Value);
    }

    /// <summary>三击越界行 — 整行扫描 no-op 兜底 focus=anchor 单 cell（selectLineAt 越界 + ink.tsx:1338 兜底）。</summary>
    [Fact]
    public void TripleClickOutOfBoundsRow_FallsBackToPoint()
    {
        var screen = ScreenOf(10, "abc");
        var time = 0L;
        var state = new SelectionState(() => time);

        state.HandleMouse(LeftPress(2, 9), screen);
        state.HandleMouse(LeftPress(2, 9), screen);
        state.HandleMouse(LeftPress(2, 9), screen);

        Assert.True(state.HasSelection);
        Assert.Equal(new SelectionBounds(new SelectionPoint(2, 9), new SelectionPoint(2, 9)), state.Bounds!.Value);
    }

    /// <summary>未 Press 直接 Drag — no-op（selection.ts:105：拖拽外不更新）。</summary>
    [Fact]
    public void DragWithoutPress_Ignored()
    {
        var screen = ScreenOf(20, "hello world");
        var state = new SelectionState();

        state.HandleMouse(LeftDrag(3, 0), screen);

        Assert.False(state.HasSelection);
        Assert.False(state.IsSelecting);
        Assert.Null(state.Bounds);
    }
}
