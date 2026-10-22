namespace NativeTui.Tests.Repl;

/// <summary>
/// 粘贴内容登记簿测试 — 对齐基准 onTextPaste 折叠（&gt;800 字符或行数超限 → [Pasted text #N +M lines]）、
/// inputPaste.ts 截断（&gt;10000 字符 → 前/后 500 + [...Truncated text #N +M lines...]）、
/// history.ts 展开（expandPastedTextRefs 提交全文）与清洗链（stripAnsi / \r 归一 / Tab 展开）。
/// </summary>
public sealed class InputPastedContentTrackerTests
{
    [Fact]
    public void ShortSingleLinePaste_ReturnedVerbatimWithoutRegistration()
    {
        var tracker = new PastedContentTracker();
        var inserted = tracker.RegisterPaste("hello");
        Assert.Equal("hello", inserted);
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void LongPaste_OverThreshold_FoldsIntoPastedTextRef()
    {
        var tracker = new PastedContentTracker();
        var longText = new string('a', PastedContentTracker.PasteThreshold + 1);
        var inserted = tracker.RegisterPaste(longText);
        Assert.Equal("[Pasted text #1]", inserted);
        Assert.Equal(1, tracker.Count);
        Assert.Equal(longText, tracker.TryGetContent(1));
    }

    [Fact]
    public void MultilinePaste_OverLineLimit_FoldsWithLineCount()
    {
        var tracker = new PastedContentTracker();
        var inserted = tracker.RegisterPaste("line1\nline2\nline3\nline4");
        // 4 行超默认上限 2（行计数 = 换行符数 = 3）
        Assert.Equal("[Pasted text #1 +3 lines]", inserted);
        Assert.Equal("line1\nline2\nline3\nline4", tracker.TryGetContent(1));
    }

    [Fact]
    public void FoldedPastes_NumberIncrements()
    {
        var tracker = new PastedContentTracker();
        Assert.Equal("[Pasted text #1 +3 lines]", tracker.RegisterPaste("a\nb\nc\nd"));
        Assert.Equal("[Pasted text #2 +1 lines]", tracker.RegisterPaste("x\ny", maxVisibleLines: 0));
        Assert.Equal(2, tracker.Count);
    }

    [Fact]
    public void ExactlyAtThreshold_NotFolded()
    {
        var tracker = new PastedContentTracker();
        var boundary = new string('a', PastedContentTracker.PasteThreshold);
        Assert.Equal(boundary, tracker.RegisterPaste(boundary));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void RegisterPaste_SanitizesAnsiCarriageReturnsAndTabs()
    {
        var tracker = new PastedContentTracker();
        // 含 ANSI 色彩序列 + \r\n + \t — 全部清洗后仍为短文本直插
        var inserted = tracker.RegisterPaste("\u001b[31mred\u001b[0m a\tb\r\nc");
        Assert.Equal("red a    b\nc", inserted);
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void ExpandReferences_ReplacesRegisteredRefsWithOriginalText()
    {
        var tracker = new PastedContentTracker();
        tracker.RegisterPaste("line1\nline2\nline3\nline4");
        var expanded = tracker.ExpandReferences("see [Pasted text #1 +3 lines] here");
        Assert.Equal("see line1\nline2\nline3\nline4 here", expanded);
    }

    [Fact]
    public void ExpandReferences_UnregisteredRefKeptVerbatim()
    {
        var tracker = new PastedContentTracker();
        Assert.Equal("typed [Pasted text #9] manually", tracker.ExpandReferences("typed [Pasted text #9] manually"));
    }

    [Fact]
    public void ExpandReferences_ImageRefKeptVerbatim()
    {
        var tracker = new PastedContentTracker();
        tracker.RegisterPaste("long text ".PadRight(801, 'x'));
        var expanded = tracker.ExpandReferences("[Pasted text #1] and [Image #1]");
        Assert.Equal($"{tracker.TryGetContent(1)} and [Image #1]", expanded);
    }

    [Fact]
    public void ExpandReferences_MultipleRefsAllExpanded()
    {
        var tracker = new PastedContentTracker();
        tracker.RegisterPaste("AAA\nBBB", maxVisibleLines: 0);
        tracker.RegisterPaste("CCC\nDDD", maxVisibleLines: 0);
        var expanded = tracker.ExpandReferences("[Pasted text #1 +1 lines] | [Pasted text #2 +1 lines]");
        Assert.Equal("AAA\nBBB | CCC\nDDD", expanded);
    }

    [Fact]
    public void TruncateIfTooLong_UnderThreshold_ReturnsInputUnchanged()
    {
        var tracker = new PastedContentTracker();
        var input = new string('a', PastedContentTracker.TruncationThreshold);
        Assert.Equal(input, tracker.TruncateIfTooLong(input));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void TruncateIfTooLong_OverThreshold_KeepsHeadTailAndRegistersMiddle()
    {
        var tracker = new PastedContentTracker();
        var input = new string('x', PastedContentTracker.TruncationThreshold + 1000);
        var truncated = tracker.TruncateIfTooLong(input);

        var half = PastedContentTracker.PreviewLength / 2;
        Assert.Equal(input[..half], truncated[..half]);
        Assert.Equal(input[^half..], truncated[^half..]);
        Assert.StartsWith($"[...Truncated text #1 +0 lines...]", truncated[half..]);
        Assert.Equal(1, tracker.Count);
        Assert.Equal(input.Length, half * 2 + tracker.TryGetContent(1)!.Length);
    }

    [Fact]
    public void TruncateIfTooLong_MiddleWithNewlines_CountsTruncatedLines()
    {
        var tracker = new PastedContentTracker();
        // 前 500 + 中段（4 个换行）+ 后 500，超阈值 10000
        var middle = "a\nb\nc\nd\ne" + new string('m', PastedContentTracker.TruncationThreshold);
        var input = new string('h', 500) + middle + new string('t', 500);
        var truncated = tracker.TruncateIfTooLong(input);

        Assert.Contains("[...Truncated text #1 +4 lines...]", truncated);
        Assert.Equal(middle, tracker.TryGetContent(1));
    }

    [Fact]
    public void Clear_EmptiesRegistrations()
    {
        var tracker = new PastedContentTracker();
        tracker.RegisterPaste("a\nb\nc\nd");
        tracker.Clear();
        Assert.Equal(0, tracker.Count);
        Assert.Null(tracker.TryGetContent(1));
        // 清空后编号重新从 1 开始
        Assert.Equal("[Pasted text #1 +3 lines]", tracker.RegisterPaste("w\nx\ny\nz"));
    }

    [Fact]
    public void CountNewlines_CountsSeparatorsNotSegments()
    {
        // 基准语义："a\nb\nc" 计 2 非 3
        Assert.Equal(0, PastedContentTracker.CountNewlines("abc"));
        Assert.Equal(2, PastedContentTracker.CountNewlines("a\nb\nc"));
        Assert.Equal(1, PastedContentTracker.CountNewlines("a\r\nb"));
    }

    [Fact]
    public void ClearViaInputBufferSubmit_RestoresSingleLineSemantics()
    {
        var buffer = new InputBuffer();
        buffer.Paste(new string('p', PastedContentTracker.PasteThreshold + 1));
        Assert.Equal("[Pasted text #1]", buffer.Text);
        Assert.Equal(new string('p', PastedContentTracker.PasteThreshold + 1), buffer.ExpandedText);

        buffer.Clear();
        Assert.Equal(0, buffer.Text.Length);
        Assert.Equal(string.Empty, buffer.ExpandedText);

        // 清空后新粘贴重新从 #1 编号
        buffer.Paste("a\nb\nc\nd");
        Assert.Equal("[Pasted text #1 +3 lines]", buffer.Text);
    }
}
