namespace NativeTui.Tests.Repl;

/// <summary>
/// 输入缓冲词编辑测试 — 对齐基准 utils/Cursor.ts 的词移动/kill ring 语义：
/// 词 = 字母数字连续段（CJK 每字自成一词），kill ring 全局共享（MaxSize 10），
/// 连续 kill 累积（append/prepend），Ctrl+Y yank、Alt+Y yank-pop 替换。
/// </summary>
/// <summary>KillRing 静态单例串行编排（全局态断言跨类并行碰撞根治 — 同 Collection 类串行执行）。</summary>
[Collection("kill-ring")]
public sealed class InputBufferWordTests
{
    public InputBufferWordTests()
    {
        KillRing.Clear();
    }

    [Fact]
    public void WordLeft_FromInsideWord_MovesToWordStart()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 8);
        buffer.WordLeft();
        Assert.Equal(6, buffer.CursorCol);
        Assert.Equal("hello world", buffer.Text);
    }

    [Fact]
    public void WordLeft_AtWordStart_SkipsToPreviousWordStart()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 6);
        buffer.WordLeft();
        Assert.Equal(0, buffer.CursorCol);
    }

    [Fact]
    public void WordLeft_AtStart_NoOp()
    {
        var buffer = InputBuffer.Of("hello", cursorCol: 0);
        buffer.WordLeft();
        Assert.Equal(0, buffer.CursorCol);
    }

    [Fact]
    public void WordRight_FromInsideWord_MovesToNextWordStart()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 2);
        buffer.WordRight();
        Assert.Equal(6, buffer.CursorCol);
        Assert.Equal("hello world", buffer.Text);
    }

    [Fact]
    public void WordRight_AtEnd_NoOp()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 11);
        buffer.WordRight();
        Assert.Equal(11, buffer.CursorCol);
    }

    [Fact]
    public void WordMovement_SkipsPunctuation()
    {
        var buffer = InputBuffer.Of("foo-bar baz", cursorCol: 7);
        // 8.14 增强 — kebab-case 整词（连字符并入词字符）：WordLeft 跳整词到 0
        buffer.WordLeft();
        Assert.Equal(0, buffer.CursorCol);

        buffer.WordRight();
        Assert.Equal(8, buffer.CursorCol);
    }

    [Fact]
    public void WordMovement_CjkCharsAreSingleWords()
    {
        var buffer = InputBuffer.Of("你好世界", cursorCol: 3);
        buffer.WordLeft();
        Assert.Equal(2, buffer.CursorCol);

        buffer.WordLeft();
        Assert.Equal(1, buffer.CursorCol);

        buffer.WordRight();
        Assert.Equal(2, buffer.CursorCol);
    }

    [Fact]
    public void WordMovement_MixedCjkAndLatin()
    {
        var buffer = InputBuffer.Of("你好 abc", cursorCol: 4);
        buffer.WordLeft();
        Assert.Equal(3, buffer.CursorCol);

        buffer.WordLeft();
        Assert.Equal(1, buffer.CursorCol);

        buffer.WordLeft();
        Assert.Equal(0, buffer.CursorCol);
    }

    [Fact]
    public void KillToLineEnd_RemovesAndPushesRing()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 5);
        buffer.KillToLineEnd();
        Assert.Equal("hello", buffer.Text);
        Assert.Equal(5, buffer.CursorCol);
        Assert.Equal(" world", KillRing.Last);
    }

    [Fact]
    public void KillToLineStart_RemovesAndPushesRing()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 5);
        buffer.KillToLineStart();
        Assert.Equal(" world", buffer.Text);
        Assert.Equal(0, buffer.CursorCol);
        Assert.Equal("hello", KillRing.Last);
    }

    [Fact]
    public void KillWordBefore_FromWordMiddle_CutsWordPart()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 8);
        buffer.KillWordBefore();
        Assert.Equal("hello rld", buffer.Text);
        Assert.Equal(6, buffer.CursorCol);
        Assert.Equal("wo", KillRing.Last);
    }

    [Fact]
    public void KillWordBefore_FromWordStart_CutsPreviousWord()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 6);
        buffer.KillWordBefore();
        Assert.Equal("world", buffer.Text);
        Assert.Equal(0, buffer.CursorCol);
        Assert.Equal("hello ", KillRing.Last);
    }

    [Fact]
    public void KillWordAfter_RemovesWordAfterCursor_NoRingEntry()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 6);
        buffer.KillWordAfter();
        Assert.Equal("hello ", buffer.Text);
        Assert.Equal(6, buffer.CursorCol);
        Assert.Equal(string.Empty, KillRing.Last);
    }

    [Fact]
    public void KillRing_ConsecutiveAppend_Accumulates()
    {
        KillRing.Push(" world", prepend: false);
        KillRing.Push("hello", prepend: false);
        Assert.Equal(" worldhello", KillRing.Last);
        Assert.Equal(1, KillRing.Size);
    }

    [Fact]
    public void ConsecutiveKillWordBefore_PrependAccumulates()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 11);
        buffer.KillWordBefore();
        Assert.Equal("hello ", buffer.Text);
        buffer.KillWordBefore();
        Assert.Equal(string.Empty, buffer.Text);
        Assert.Equal("hello world", KillRing.Last);
        Assert.Equal(1, KillRing.Size);
    }

    [Fact]
    public void KillAccumulation_BrokenByNonKillAction()
    {
        var buffer = InputBuffer.Of("hello world", cursorCol: 5);
        buffer.KillToLineEnd();
        buffer.Insert('x');
        buffer.Home();
        buffer.KillToLineEnd();
        Assert.Equal(2, KillRing.Size);
        Assert.Equal("hellox", KillRing.Last);
    }

    [Fact]
    public void KillRing_CappedAtTen()
    {
        for (var i = 0; i < 12; i++)
        {
            KillRing.ResetKillAccumulation();
            KillRing.Push("k" + i, prepend: false);
        }

        Assert.Equal(10, KillRing.Size);
        Assert.Equal("k11", KillRing.Last);
    }

    [Fact]
    public void Yank_InsertsLastKillAtCursor()
    {
        KillRing.Push("pasted", prepend: false);
        var buffer = InputBuffer.Of("hello world", cursorCol: 6);
        buffer.Yank();
        Assert.Equal("hello pastedworld", buffer.Text);
        Assert.Equal(12, buffer.CursorCol);
    }

    [Fact]
    public void YankPop_ReplacesPreviousYankWithNextRingItem()
    {
        KillRing.Push("first", prepend: false);
        KillRing.ResetKillAccumulation();
        KillRing.Push("second", prepend: false);
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        buffer.Yank();
        Assert.Equal("asecondb", buffer.Text);
        Assert.Equal(7, buffer.CursorCol);

        var popped = buffer.YankPop();
        Assert.True(popped);
        Assert.Equal("afirstb", buffer.Text);
        Assert.Equal(6, buffer.CursorCol);
    }

    [Fact]
    public void YankPop_WithoutPreviousYank_ReturnsFalse()
    {
        KillRing.Push("first", prepend: false);
        KillRing.ResetKillAccumulation();
        KillRing.Push("second", prepend: false);
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        Assert.False(buffer.YankPop());
        Assert.Equal("ab", buffer.Text);
    }

    [Fact]
    public void YankPop_SingleRingItem_ReturnsFalse()
    {
        KillRing.Push("only", prepend: false);
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        buffer.Yank();
        Assert.False(buffer.YankPop());
        Assert.Equal("aonlyb", buffer.Text);
    }

    [Fact]
    public void ResetYankState_AfterYank_YankPopReturnsFalse()
    {
        KillRing.Push("first", prepend: false);
        KillRing.ResetKillAccumulation();
        KillRing.Push("second", prepend: false);
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        buffer.Yank();
        buffer.ResetYankState();
        Assert.False(buffer.YankPop());
    }

    [Fact]
    public void Yank_EmptyRing_NoOp()
    {
        var buffer = InputBuffer.Of("ab", cursorCol: 1);
        buffer.Yank();
        Assert.Equal("ab", buffer.Text);
        Assert.Equal(1, buffer.CursorCol);
    }
}
