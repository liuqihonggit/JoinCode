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

    /// <summary>CR LF 单 cluster：退格删整对（不留孤立 CR）。</summary>
    [Fact]
    public void Backspace_CrLf_DeletesPair()
    {
        var buffer = InputBuffer.Of("a\r\nb", cursorCol: 3);
        buffer.Backspace();
        Assert.Equal("ab", buffer.Text);
    }
}
