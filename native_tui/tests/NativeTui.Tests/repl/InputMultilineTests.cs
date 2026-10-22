namespace NativeTui.Tests.Repl;

/// <summary>
/// 多行编辑测试 — 对齐基准 PromptInput.tsx multiline + utils.ts shift+enter 换行：
/// InsertNewline 拆逻辑行、LineOf/ColumnOf 行内定位、MoveCursorVertically 跨行保列、
/// Home/End/Kill 行内语义；单行降级（无换行）与既有单行行为逐字节一致。
/// </summary>
/// <summary>KillRing 静态单例串行编排（写方入组 — KillToLine* 写静态 KillRing 污染读方断言；
/// S12 未决#2 根治：与 WordTests/UndoTests 同 Collection 串行）。</summary>
[Collection("kill-ring")]
public sealed class InputMultilineTests
{
    [Fact]
    public void InsertNewline_SplitsLineAndMovesCursorToNewLineStart()
    {
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        buffer.InsertNewline();
        Assert.Equal("a\nb", buffer.Text);
        Assert.Equal(2, buffer.CursorCol);
        Assert.Equal(1, buffer.CursorLineIndex);
        Assert.Equal(0, buffer.CursorLineColumn);
    }

    [Fact]
    public void InsertNewline_AtEmptyBuffer_CreatesTwoEmptyLines()
    {
        var buffer = new InputBuffer();
        buffer.InsertNewline();
        Assert.Equal("\n", buffer.Text);
        Assert.Equal(2, buffer.LineCount);
        Assert.Equal(1, buffer.CursorLineIndex);
    }

    [Fact]
    public void LineCount_CountsNewlineSplits()
    {
        Assert.Equal(1, InputBuffer.Of("abc", 0).LineCount);
        Assert.Equal(2, InputBuffer.Of("a\nb", 0).LineCount);
        Assert.Equal(3, InputBuffer.Of("a\nb\nc", 0).LineCount);
        Assert.Equal(1, new InputBuffer().LineCount);
    }

    [Theory]
    [InlineData("a\nbc", 0, 0, 0)]
    [InlineData("a\nbc", 1, 0, 1)]
    [InlineData("a\nbc", 2, 1, 0)]
    [InlineData("a\nbc", 4, 1, 2)]
    [InlineData("a\nb\nc", 4, 2, 0)]
    public void LineOf_And_ColumnOf_LocateOffsetInsideLogicalLine(string text, int offset, int expectedLine, int expectedColumn)
    {
        var buffer = InputBuffer.Of(text, 0);
        Assert.Equal(expectedLine, buffer.LineOf(offset));
        Assert.Equal(expectedColumn, buffer.ColumnOf(offset));
    }

    [Fact]
    public void SingleLine_DegradesToFlatSemantics()
    {
        var buffer = InputBuffer.Of("abc", 2);
        Assert.False(buffer.IsMultiline);
        Assert.Equal(1, buffer.LineCount);
        Assert.Equal(0, buffer.CursorLineIndex);
        Assert.Equal(2, buffer.CursorLineColumn);
        Assert.Equal(0, buffer.LineOf(3));
        Assert.Equal(3, buffer.ColumnOf(3));
    }

    [Fact]
    public void MoveCursorVertically_PreservesColumnAndClampsToShorterLine()
    {
        var buffer = InputBuffer.Of("hello\nab\nlongline", cursorCol: 2);
        Assert.Equal(0, buffer.CursorLineIndex);
        Assert.Equal(2, buffer.CursorLineColumn);

        buffer.MoveCursorVertically(1);
        Assert.Equal(1, buffer.CursorLineIndex);
        Assert.Equal(2, buffer.CursorLineColumn);

        buffer.MoveCursorVertically(-1);
        Assert.Equal(0, buffer.CursorLineIndex);
        Assert.Equal(2, buffer.CursorLineColumn);

        // 长列（行尾列 5）移到短行（"ab" 长 2）钳制行尾
        buffer.End();
        Assert.Equal(5, buffer.CursorLineColumn);
        buffer.MoveCursorVertically(1);
        Assert.Equal(1, buffer.CursorLineIndex);
        Assert.Equal(2, buffer.CursorLineColumn);

        // 长行方向列保持
        buffer.MoveCursorVertically(1);
        Assert.Equal(2, buffer.CursorLineIndex);
        Assert.Equal(2, buffer.CursorLineColumn);
    }

    [Fact]
    public void MoveCursorVertically_OutOfBounds_NoOp()
    {
        var buffer = InputBuffer.Of("a\nb", cursorCol: 3);
        buffer.MoveCursorVertically(1);
        Assert.Equal(3, buffer.CursorCol);

        buffer.Home();
        Assert.Equal(2, buffer.CursorCol);
        buffer.MoveCursorVertically(1);
        Assert.Equal(2, buffer.CursorCol);

        var topBuffer = InputBuffer.Of("a\nb", cursorCol: 1);
        topBuffer.MoveCursorVertically(-1);
        Assert.Equal(1, topBuffer.CursorCol);
    }

    [Fact]
    public void MoveCursorVertically_Zero_NoOp()
    {
        var buffer = InputBuffer.Of("a\nb", cursorCol: 3);
        buffer.MoveCursorVertically(0);
        Assert.Equal(3, buffer.CursorCol);
    }

    [Fact]
    public void HomeEnd_MoveWithinCurrentLine()
    {
        var buffer = InputBuffer.Of("aa\nbbbb", cursorCol: 5);
        buffer.Home();
        Assert.Equal(3, buffer.CursorCol);
        Assert.Equal(1, buffer.CursorLineIndex);
        buffer.End();
        Assert.Equal(7, buffer.CursorCol);
    }

    [Fact]
    public void HomeEnd_OnFirstLine_MatchSingleLineSemantics()
    {
        var buffer = InputBuffer.Of("abc", cursorCol: 2);
        buffer.Home();
        Assert.Equal(0, buffer.CursorCol);
        buffer.End();
        Assert.Equal(3, buffer.CursorCol);
    }

    [Fact]
    public void LeftRight_CrossNewlineAsSingleOffset()
    {
        var buffer = InputBuffer.Of("a\nb", cursorCol: 2);
        buffer.Left();
        Assert.Equal(1, buffer.CursorCol);
        buffer.Left();
        Assert.Equal(0, buffer.CursorCol);
        buffer.Right();
        buffer.Right();
        Assert.Equal(2, buffer.CursorCol);
    }

    [Fact]
    public void Backspace_AtLineStart_MergesLines()
    {
        var buffer = InputBuffer.Of("aa\nbb", cursorCol: 3);
        buffer.Backspace();
        Assert.Equal("aabb", buffer.Text);
        Assert.Equal(2, buffer.CursorCol);
    }

    [Fact]
    public void KillToLineEnd_OnlyAffectsCurrentLogicalLine()
    {
        var buffer = InputBuffer.Of("aa\nbbbb", cursorCol: 4);
        buffer.KillToLineEnd();
        Assert.Equal("aa\nb", buffer.Text);
        Assert.Equal(4, buffer.CursorCol);
    }

    [Fact]
    public void KillToLineStart_OnlyAffectsCurrentLogicalLine()
    {
        var buffer = InputBuffer.Of("aa\nbbbb", cursorCol: 7);
        buffer.KillToLineStart();
        Assert.Equal("aa\n", buffer.Text);
        Assert.Equal(3, buffer.CursorCol);
    }

    [Fact]
    public void LineText_ReturnsLineWithoutNewline()
    {
        var buffer = InputBuffer.Of("aa\nbb\ncc", cursorCol: 0);
        Assert.Equal("aa", buffer.LineText(0));
        Assert.Equal("bb", buffer.LineText(1));
        Assert.Equal("cc", buffer.LineText(2));
    }

    [Fact]
    public void LineStartOffset_MapsLineIndexToTextOffset()
    {
        var buffer = InputBuffer.Of("aa\nbb\ncc", cursorCol: 0);
        Assert.Equal(0, buffer.LineStartOffset(0));
        Assert.Equal(3, buffer.LineStartOffset(1));
        Assert.Equal(6, buffer.LineStartOffset(2));
    }

    [Fact]
    public void Replace_KeepsMultilineTextAndClampsCursor()
    {
        var buffer = InputBuffer.Of("old", cursorCol: 2);
        buffer.Replace("a\nb\nc");
        Assert.Equal("a\nb\nc", buffer.Text);
        Assert.Equal("a\nb\nc".Length, buffer.CursorCol);
        Assert.True(buffer.IsMultiline);
    }
}
