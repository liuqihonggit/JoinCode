namespace Hands.Tests.ToolHandlers;

/// <summary>
/// 行号前缀格式单元测试。
/// 验证 AddLineNumbers 的紧凑(\t)/宽(→)两种输出格式，以及 StripLineNumberPrefixes 的逆剥离。
/// 对齐 TS: addLineNumbers / stripLineNumberPrefix。
/// </summary>
public class LineNumberPrefixTests {
    [Fact]
    public void AddLineNumbers_Compact_UsesTabSeparator() {
        var result = FileToolHandlers.AddLineNumbers("a\nb", 1, compact: true);
        result.Should().Be($"1\ta{Environment.NewLine}2\tb{Environment.NewLine}");
    }

    [Fact]
    public void AddLineNumbers_Wide_UsesArrowSeparator() {
        var result = FileToolHandlers.AddLineNumbers("a\nb", 1, compact: false);
        result.Should().Be($"     1\u2192a{Environment.NewLine}     2\u2192b{Environment.NewLine}");
    }

    [Fact]
    public void AddLineNumbers_EmptyContent_ReturnsEmpty() {
        FileToolHandlers.AddLineNumbers("", 1, compact: true).Should().BeEmpty();
        FileToolHandlers.AddLineNumbers("", 1, compact: false).Should().BeEmpty();
    }

    [Fact]
    public void AddLineNumbers_WithStartLine_PreservesOffset() {
        var result = FileToolHandlers.AddLineNumbers("a", 5, compact: true);
        result.Should().Be($"5\ta{Environment.NewLine}");
    }

    [Fact]
    public void AddLineNumbers_Wide_PadWidthAtLeastSix() {
        var result = FileToolHandlers.AddLineNumbers("a", 1, compact: false);
        result.Should().Be($"     1\u2192a{Environment.NewLine}");
    }

    [Fact]
    public void StripLineNumberPrefixes_CompactPrefix_Strips() {
        FileEditor.StripLineNumberPrefixes("1\ta\n2\tb").Should().Be("a\nb");
    }

    [Fact]
    public void StripLineNumberPrefixes_WidePrefix_Strips() {
        FileEditor.StripLineNumberPrefixes("     1\u2192a\n     2\u2192b").Should().Be("a\nb");
    }

    [Fact]
    public void StripLineNumberPrefixes_NoPrefix_ReturnsOriginal() {
        FileEditor.StripLineNumberPrefixes("a\nb").Should().Be("a\nb");
    }

    [Fact]
    public void StripLineNumberPrefixes_PartialPrefix_StripsOnlyPrefixedLines() {
        FileEditor.StripLineNumberPrefixes("1\ta\nb").Should().Be("a\nb");
    }

    [Fact]
    public void StripLineNumberPrefixes_Empty_ReturnsEmpty() {
        FileEditor.StripLineNumberPrefixes("").Should().Be("");
    }

    [Fact]
    public void StripLineNumberPrefixes_CodeWithTabIndent_NotStripped() {
        // 制表符缩进的代码行不以"数字+分隔符"开头，不应被误剥离
        FileEditor.StripLineNumberPrefixes("\tindented code").Should().Be("\tindented code");
    }

    [Fact]
    public void LineNumberFormatter_Format_Compact_UsesTabSeparator() {
        LineNumberFormatter.Format(1, "content", compact: true).Should().Be("1\tcontent");
        LineNumberFormatter.Format(42, "log line", compact: true).Should().Be("42\tlog line");
    }

    [Fact]
    public void LineNumberFormatter_Format_Wide_UsesArrowWithPadStart6() {
        LineNumberFormatter.Format(1, "content", compact: false).Should().Be("     1\u2192content");
        LineNumberFormatter.Format(42, "log line", compact: false).Should().Be("    42\u2192log line");
    }

    [Fact]
    public void LineNumberFormatter_Format_Wide_LargeLineNumber_NoPadding() {
        LineNumberFormatter.Format(123456, "content", compact: false).Should().Be("123456\u2192content");
        LineNumberFormatter.Format(9999999, "content", compact: false).Should().Be("9999999\u2192content");
    }

    [Fact]
    public void LineNumberFormatter_FormatMultiLine_Compact_UsesTabSeparator() {
        var result = LineNumberFormatter.FormatMultiLine("a\nb", 1, compact: true);
        result.Should().Be($"1\ta{Environment.NewLine}2\tb{Environment.NewLine}");
    }

    [Fact]
    public void LineNumberFormatter_FormatMultiLine_Wide_UsesArrowSeparator() {
        var result = LineNumberFormatter.FormatMultiLine("a\nb", 1, compact: false);
        result.Should().Be($"     1\u2192a{Environment.NewLine}     2\u2192b{Environment.NewLine}");
    }

    [Fact]
    public void LineNumberFormatter_FormatMultiLine_EmptyContent_ReturnsEmpty() {
        LineNumberFormatter.FormatMultiLine("", 1, compact: true).Should().BeEmpty();
        LineNumberFormatter.FormatMultiLine("", 1, compact: false).Should().BeEmpty();
    }

    [Fact]
    public void LineRangeReader_Slice_BasicTruncation() {
        var lines = new[] { "a", "b", "c", "d", "e" };
        var (range, hasMore, nextSkip) = LineRangeReader.Slice(lines, skipLines: 1, maxLines: 2);
        range.Should().Equal("b", "c");
        hasMore.Should().BeTrue();
        nextSkip.Should().Be(3);
    }

    [Fact]
    public void LineRangeReader_Slice_SkipAll_ReturnsEmpty() {
        var lines = new[] { "a", "b" };
        var (range, hasMore, nextSkip) = LineRangeReader.Slice(lines, skipLines: 5, maxLines: 10);
        range.Should().BeEmpty();
        hasMore.Should().BeFalse();
        nextSkip.Should().Be(5);
    }

    [Fact]
    public void LineRangeReader_Read_Compact_UsesTabSeparator() {
        var lines = new[] { "first", "second" };
        var result = LineRangeReader.Read(lines, skipLines: 0, maxLines: 10, compactLinePrefix: true);
        result.Text.Should().Contain("1\tfirst");
        result.Text.Should().Contain("2\tsecond");
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public void LineRangeReader_Read_Wide_UsesArrowSeparator() {
        var lines = new[] { "first", "second" };
        var result = LineRangeReader.Read(lines, skipLines: 0, maxLines: 10, compactLinePrefix: false);
        result.Text.Should().Contain("     1\u2192first");
        result.Text.Should().Contain("     2\u2192second");
    }

    [Fact]
    public void LineRangeReader_Read_HasMore_ContinueHint() {
        var lines = new[] { "a", "b", "c", "d", "e" };
        var result = LineRangeReader.Read(lines, skipLines: 0, maxLines: 2, compactLinePrefix: true);
        result.HasMore.Should().BeTrue();
        result.NextSkip.Should().Be(2);
        result.Text.Should().Contain("skip_lines=2");
    }

    [Fact]
    public void LineRangeReader_Read_EmptyLines_ReturnsEmpty() {
        var result = LineRangeReader.Read([], skipLines: 0, maxLines: 10, compactLinePrefix: true);
        result.Text.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public void LineRangeReader_Read_WithSkip_LineNumbersAreGlobal() {
        var lines = new[] { "a", "b", "c", "d", "e" };
        var result = LineRangeReader.Read(lines, skipLines: 2, maxLines: 2, compactLinePrefix: true);
        result.Text.Should().Contain("3\tc");
        result.Text.Should().Contain("4\td");
    }
}