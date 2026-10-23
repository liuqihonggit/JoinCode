namespace NativeTui.Tests.Repl;

/// <summary>
/// 输入缓冲编辑器测试 — 对齐基准 hooks/useTextInput 的编辑语义：
/// 光标为文本内偏移（0..len），插入/删除/移动全部钳制边界。
/// </summary>
public sealed class InputBufferTests
{
    [Fact]
    public void Insert_AtCursor_AdvancesCursor()
    {
        var buffer = new InputBuffer();
        buffer.Insert('a');
        buffer.Insert('b');
        buffer.Insert('c');
        Assert.Equal("abc", buffer.Text);
        Assert.Equal(3, buffer.CursorCol);

        buffer.Left();
        buffer.Insert('X');
        Assert.Equal("abXc", buffer.Text);
        Assert.Equal(3, buffer.CursorCol);
    }

    [Fact]
    public void Backspace_RemovesCharBeforeCursor()
    {
        var buffer = InputBuffer.Of("abc", cursorCol: 3);
        buffer.Backspace();
        Assert.Equal("ab", buffer.Text);
        Assert.Equal(2, buffer.CursorCol);

        // 光标 0 时无操作
        buffer.Home();
        buffer.Backspace();
        Assert.Equal("ab", buffer.Text);
        Assert.Equal(0, buffer.CursorCol);
    }

    [Fact]
    public void Delete_RemovesCharAtCursor()
    {
        var buffer = InputBuffer.Of("abc", cursorCol: 1);
        buffer.Delete();
        Assert.Equal("ac", buffer.Text);
        Assert.Equal(1, buffer.CursorCol);

        // 光标在末尾时无操作
        buffer.End();
        buffer.Delete();
        Assert.Equal("ac", buffer.Text);
        Assert.Equal(2, buffer.CursorCol);
    }

    [Fact]
    public void Move_CursorClamped()
    {
        var buffer = InputBuffer.Of("abc", cursorCol: 1);
        buffer.Left();
        Assert.Equal(0, buffer.CursorCol);
        buffer.Left();
        Assert.Equal(0, buffer.CursorCol);
        buffer.Right();
        buffer.Right();
        buffer.Right();
        Assert.Equal(3, buffer.CursorCol);
        buffer.Right();
        Assert.Equal(3, buffer.CursorCol);
    }

    [Fact]
    public void HomeEnd_JumpToBoundaries()
    {
        var buffer = InputBuffer.Of("abc", cursorCol: 2);
        buffer.Home();
        Assert.Equal(0, buffer.CursorCol);
        buffer.End();
        Assert.Equal(3, buffer.CursorCol);
    }

    [Fact]
    public void Clear_ResetsTextAndCursor()
    {
        var buffer = InputBuffer.Of("abc", cursorCol: 2);
        buffer.Clear();
        Assert.Equal(string.Empty, buffer.Text);
        Assert.Equal(0, buffer.CursorCol);
    }

    [Fact]
    public void EmptyBuffer_AllOperationsSafe()
    {
        var buffer = new InputBuffer();
        buffer.Backspace();
        buffer.Delete();
        buffer.Left();
        buffer.Right();
        buffer.Home();
        buffer.End();
        Assert.Equal(string.Empty, buffer.Text);
        Assert.Equal(0, buffer.CursorCol);
    }

    [Fact]
    public void Replace_SetsTextAndCursorToEnd()
    {
        var buffer = InputBuffer.Of("old", cursorCol: 2);
        buffer.Replace("new text");
        Assert.Equal("new text", buffer.Text);
        Assert.Equal("new text".Length, buffer.CursorCol);
    }

    [Fact]
    public void Replace_CursorToStart_PutsCursorAtZero()
    {
        var buffer = InputBuffer.Of("old", cursorCol: 2);
        buffer.Replace("new text", cursorToStart: true);
        Assert.Equal("new text", buffer.Text);
        Assert.Equal(0, buffer.CursorCol);
    }

    /// <summary>grapheme 光标模型（DSG044 L2 — 基准 Cursor.ts prevOffset/nextOffset 跳 grapheme 边界）：
    /// emoji 代理对一次方向键跨整字（不停代理对中间）。</summary>
    [Fact]
    public void LeftRight_SurrogatePair_MovesByGrapheme()
    {
        var buffer = InputBuffer.Of("a😀b", cursorCol: 4);
        buffer.Left();
        Assert.Equal(3, buffer.CursorCol);
        buffer.Left();
        Assert.Equal(1, buffer.CursorCol);
        buffer.Left();
        Assert.Equal(0, buffer.CursorCol);

        buffer.Right();
        Assert.Equal(1, buffer.CursorCol);
        buffer.Right();
        Assert.Equal(3, buffer.CursorCol);
        buffer.Right();
        Assert.Equal(4, buffer.CursorCol);
    }

    /// <summary>退格删整 grapheme（emoji 代理对一次删完整 — 不留半个代理对）。</summary>
    [Fact]
    public void Backspace_SurrogatePair_DeletesWholeGrapheme()
    {
        var buffer = InputBuffer.Of("a😀b", cursorCol: 4);
        buffer.Backspace();
        Assert.Equal("a😀", buffer.Text);
        buffer.Backspace();
        Assert.Equal("a", buffer.Text);
        Assert.Equal(1, buffer.CursorCol);
    }

    /// <summary>ZWJ emoji 序列（家庭组合单 cluster）一次退格删完整序列。</summary>
    [Fact]
    public void Backspace_ZwjChain_DeletesWholeCluster()
    {
        var family = "👨‍👩‍👧";
        var buffer = InputBuffer.Of(family, cursorCol: family.Length);
        buffer.Backspace();
        Assert.Equal(string.Empty, buffer.Text);
    }

    /// <summary>组合符附加（e+U+0301 单 cluster）一次退格删完整 cluster。</summary>
    [Fact]
    public void Backspace_CombiningMark_DeletesWholeCluster()
    {
        var buffer = InputBuffer.Of("éx", cursorCol: 3);
        buffer.Backspace();
        Assert.Equal("é", buffer.Text);
        buffer.Backspace();
        Assert.Equal(string.Empty, buffer.Text);
    }

    /// <summary>undo 防抖分组（8.20 — 基准 useInputBuffer debounceMs=1000：1s 内连续编辑归一组，
    /// 一次 Undo 回退整组；跨组分别回退）。</summary>
    [Fact]
    public void Undo_DebounceGroup_ConsecutiveEditsOneStep()
    {
        var clock = 10_000L;
        var buffer = new InputBuffer();
        buffer.UndoClock = () => clock;

        buffer.Insert('a');
        buffer.Insert('b');
        buffer.Insert('c');
        Assert.Equal("abc", buffer.Text);

        // 同 1s 桶内三键一组 — 一次 undo 回退整组到空
        Assert.True(buffer.Undo());
        Assert.Equal(string.Empty, buffer.Text);

        buffer.Insert('x');
        clock += 2000;
        buffer.Insert('y');
        Assert.Equal("xy", buffer.Text);

        // 跨桶两组 — 两次 undo 分别回退
        Assert.True(buffer.Undo());
        Assert.Equal("x", buffer.Text);
        Assert.True(buffer.Undo());
        Assert.Equal(string.Empty, buffer.Text);
    }

    /// <summary>Backspace chip 整块删除（8.16 — 基准 deleteTokenBefore：[Pasted text #N]/
    /// [Image #N] 折叠引用一次退格删整块）。</summary>
    [Fact]
    public void Backspace_PastedRefChip_DeletesWholeChip()
    {
        var buffer = InputBuffer.Of("see [Pasted text #1 +3 lines] now", cursorCol: 29);

        buffer.Backspace();

        Assert.Equal("see  now", buffer.Text);
    }

    /// <summary>chip 整删：光标在 chip 中部（非紧邻 ] 后）退格仍为普通字符删除（边界精确匹配）。</summary>
    [Fact]
    public void Backspace_MidChipText_NormalCharDelete()
    {
        var buffer = InputBuffer.Of("see [Pasted text #1] now", cursorCol: 13);

        buffer.Backspace();

        Assert.Equal("see [Pasted ext #1] now", buffer.Text);
    }

    /// <summary>词移动增强（8.14 — 基准 Intl.Segmenter word 粒度近似）：下划线/连字符并入词
    ///（snake_case/kebab-case 整词跳转，标点点缀跳过）。</summary>
    [Fact]
    public void WordMove_SnakeCase_JumpsWholeToken()
    {
        var buffer = InputBuffer.Of("my_var name", cursorCol: 11);

        buffer.WordLeft();

        Assert.Equal(7, buffer.CursorCol);
        buffer.WordLeft();
        Assert.Equal(0, buffer.CursorCol);
    }

    /// <summary>视觉行垂直移动（DSG044 L4 — 基准 useTextInput.ts:269-316 三级回退第一级）：
    /// 折行内上下键细移（同显示列，短行钳制行尾）— 40a 按 cols 20 折 3 视觉行 [0,18)/[18,36)/[36,40)。</summary>
    [Fact]
    public void TryMoveVisualVertically_WrappedLine_MovesWithinVisualLines()
    {
        var text = new string('a', 40);
        var buffer = InputBuffer.Of(text, cursorCol: 25);

        Assert.True(buffer.TryMoveVisualVertically(-1, cols: 20));
        Assert.Equal(7, buffer.CursorCol);
        Assert.True(buffer.TryMoveVisualVertically(1, cols: 20));
        Assert.Equal(25, buffer.CursorCol);
    }

    /// <summary>视觉行移动短行钳制：目标视觉行短于当前列 → 钳制行尾。</summary>
    [Fact]
    public void TryMoveVisualVertically_ShortTargetLine_ClampsToLineEnd()
    {
        var text = new string('a', 40);
        var buffer = InputBuffer.Of(text, cursorCol: 25);

        Assert.True(buffer.TryMoveVisualVertically(1, cols: 20));
        Assert.Equal(40, buffer.CursorCol);
    }

    /// <summary>首视觉行上移 / 末视觉行下移 → false（ReplShell 层走历史导航三级回退第二级）。</summary>
    [Fact]
    public void TryMoveVisualVertically_BoundaryVisualLines_ReturnsFalse()
    {
        var text = new string('a', 40);
        var top = InputBuffer.Of(text, cursorCol: 5);
        Assert.False(top.TryMoveVisualVertically(-1, cols: 20));

        var bottom = InputBuffer.Of(text, cursorCol: 38);
        Assert.False(bottom.TryMoveVisualVertically(1, cols: 20));
    }

    /// <summary>视觉行 Home/End（DSG044 L4 — 基准 Cursor.ts:426-442 视觉行语义）：
    /// 中部 → 视觉行首/尾；已在视觉行首再按 Home → 上一视觉行尾（连按上跳）。</summary>
    [Fact]
    public void HomeVisual_EndVisual_MoveWithinVisualLine()
    {
        var text = new string('a', 40);
        var buffer = InputBuffer.Of(text, cursorCol: 25);

        buffer.HomeVisual(cols: 20);
        Assert.Equal(18, buffer.CursorCol);
        buffer.EndVisual(cols: 20);
        Assert.Equal(36, buffer.CursorCol);
    }

    /// <summary>Home 连按上跳：软折行断点处（满宽折行行尾=下行首同位）位置不变 —
    /// 真实跳转发生在跨逻辑行边界（上一视觉行是上一逻辑行末行，行尾有 \n 分隔）。</summary>
    [Fact]
    public void HomeVisual_AtVisualLineStart_MovesToPreviousVisualLineEnd()
    {
        // 满宽硬切：40a cols 20 → 视觉行 1 首 = 18 = 视觉行 0 尾（同 char 位，位置不变）
        var wrapped = InputBuffer.Of(new string('a', 40), cursorCol: 18);
        wrapped.HomeVisual(cols: 20);
        Assert.Equal(18, wrapped.CursorCol);

        // 跨逻辑行：ab\ncdef 零折行 → 视觉行 1（cdef）首 = 3 → Home 跳视觉行 0 尾 = 2
        var logical = InputBuffer.Of("ab\ncdef", cursorCol: 5);
        logical.HomeVisual(cols: 20);
        Assert.Equal(3, logical.CursorCol);
        logical.HomeVisual(cols: 20);
        Assert.Equal(2, logical.CursorCol);
    }

    /// <summary>视觉行首视觉行 0 按 Home → 保持 0（全局首）。</summary>
    [Fact]
    public void HomeVisual_AtFirstVisualLineStart_StaysAtZero()
    {
        var buffer = InputBuffer.Of(new string('a', 40), cursorCol: 0);
        buffer.HomeVisual(cols: 20);
        Assert.Equal(0, buffer.CursorCol);
    }

    /// <summary>CR LF 单 cluster：退格删整对（不留孤立 CR）。</summary>
    [Fact]
    public void Backspace_CrLf_DeletesPair()
    {
        var buffer = InputBuffer.Of("a\r\nb", cursorCol: 3);
        buffer.Backspace();
        Assert.Equal("ab", buffer.Text);
    }
}
