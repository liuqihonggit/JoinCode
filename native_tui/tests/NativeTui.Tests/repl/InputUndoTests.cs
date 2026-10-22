namespace NativeTui.Tests.Repl;

/// <summary>
/// undo 栈测试 — 对齐基准 useInputBuffer（chat:undo / ctrl+_/ctrl+shift+-）：
/// 结构性编辑前压快照（text+cursor），Undo 弹栈恢复；相同文本去重；容量 100 淘汰最旧；
/// 纯移动不产生 undo 点；Clear（提交语义）联动清栈。
/// </summary>
public sealed class InputUndoTests
{
    public InputUndoTests()
    {
        KillRing.Clear();
    }

    [Fact]
    public void Insert_PushesSnapshot_UndoRestoresTextAndCursor()
    {
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        Assert.False(buffer.CanUndo);

        buffer.Insert('X');
        Assert.Equal("aXb", buffer.Text);
        Assert.True(buffer.CanUndo);

        Assert.True(buffer.Undo());
        Assert.Equal("ab", buffer.Text);
        Assert.Equal(1, buffer.CursorCol);
        Assert.False(buffer.CanUndo);
    }

    [Fact]
    public void Undo_EmptyStack_ReturnsFalse()
    {
        var buffer = new InputBuffer();
        Assert.False(buffer.Undo());
        Assert.Equal(string.Empty, buffer.Text);
    }

    [Fact]
    public void Backspace_UndoRestoresMergedCharacter()
    {
        var buffer = InputBuffer.Of("ab", cursorCol: 2);
        buffer.Backspace();
        Assert.Equal("a", buffer.Text);

        Assert.True(buffer.Undo());
        Assert.Equal("ab", buffer.Text);
        Assert.Equal(2, buffer.CursorCol);
    }

    [Fact]
    public void MultipleEdits_UndoStepByStepInReverseOrder()
    {
        var buffer = new InputBuffer();
        buffer.Insert('a');
        buffer.Insert('b');
        buffer.Insert('c');
        Assert.Equal("abc", buffer.Text);

        buffer.Undo();
        Assert.Equal("ab", buffer.Text);
        buffer.Undo();
        Assert.Equal("a", buffer.Text);
        buffer.Undo();
        Assert.Equal(string.Empty, buffer.Text);
        Assert.False(buffer.Undo());
    }

    [Fact]
    public void PureCursorMovement_DoesNotCreateUndoPoints()
    {
        var buffer = InputBuffer.Of("abc", cursorCol: 0);
        buffer.Insert('X');
        buffer.Left();
        buffer.Right();
        buffer.Home();
        buffer.End();
        buffer.WordLeft();
        buffer.WordRight();

        buffer.Undo();
        Assert.Equal("abc", buffer.Text);
        Assert.False(buffer.CanUndo);
    }

    [Fact]
    public void NoOpEdits_DoNotCreateUndoPoints()
    {
        var atStart = InputBuffer.Of("abc", cursorCol: 0);
        atStart.Backspace();
        Assert.False(atStart.CanUndo);

        var atEnd = InputBuffer.Of("abc", cursorCol: 3);
        atEnd.Delete();
        Assert.False(atEnd.CanUndo);
    }

    [Fact]
    public void DuplicateSnapshotText_Deduplicated()
    {
        var buffer = InputBuffer.Of("same", cursorCol: 0);
        buffer.Replace("same");
        buffer.Replace("same");

        Assert.True(buffer.Undo());
        Assert.False(buffer.CanUndo);
        Assert.Equal("same", buffer.Text);
    }

    [Fact]
    public void UndoStack_CappedAt100_EvictsOldest()
    {
        var buffer = new InputBuffer();
        for (var i = 0; i < 150; i++)
        {
            buffer.Insert('x');
        }

        Assert.Equal(150, buffer.Text.Length);
        var undone = 0;
        while (buffer.Undo())
        {
            undone++;
        }

        Assert.Equal(100, undone);
        Assert.Equal(50, buffer.Text.Length);
    }

    [Fact]
    public void Clear_SubmitSemantics_ClearsUndoStack()
    {
        var buffer = new InputBuffer();
        buffer.Insert('a');
        buffer.Insert('b');
        Assert.True(buffer.CanUndo);

        buffer.Clear();
        Assert.False(buffer.CanUndo);
        Assert.False(buffer.Undo());
    }

    [Fact]
    public void ClearUndo_ExternalReset_WithoutTouchingText()
    {
        var buffer = new InputBuffer();
        buffer.Insert('a');
        buffer.ClearUndo();
        Assert.Equal("a", buffer.Text);
        Assert.False(buffer.CanUndo);
    }

    [Fact]
    public void KillToLineEnd_UndoRestoresKilledText()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 5);
        buffer.KillToLineEnd();
        Assert.Equal("hello", buffer.Text);

        Assert.True(buffer.Undo());
        Assert.Equal("hello world", buffer.Text);
        Assert.Equal(5, buffer.CursorCol);
    }

    [Fact]
    public void Yank_UndoRemovesYankedText()
    {
        KillRing.Push("pasted", prepend: false);
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        buffer.Yank();
        Assert.Equal("apastedb", buffer.Text);

        Assert.True(buffer.Undo());
        Assert.Equal("ab", buffer.Text);
        Assert.Equal(1, buffer.CursorCol);
    }

    [Fact]
    public void InsertNewline_UndoRemovesNewline()
    {
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        buffer.InsertNewline();
        Assert.Equal("a\nb", buffer.Text);

        Assert.True(buffer.Undo());
        Assert.Equal("ab", buffer.Text);
        Assert.Equal(1, buffer.CursorCol);
    }

    [Fact]
    public void Undo_CursorClampedToRestoredTextLength()
    {
        var buffer = new InputBuffer();
        buffer.Insert('a');
        buffer.Insert('b');
        buffer.Insert('c');

        buffer.Undo();
        Assert.Equal("ab", buffer.Text);
        Assert.Equal(2, buffer.CursorCol);
    }

    [Fact]
    public void Replace_UndoRestoresPreviousText()
    {
        var buffer = InputBuffer.Of("old", cursorCol: 3);
        buffer.Replace("new text");
        Assert.Equal("new text", buffer.Text);

        Assert.True(buffer.Undo());
        Assert.Equal("old", buffer.Text);
        Assert.Equal(3, buffer.CursorCol);
    }
}
