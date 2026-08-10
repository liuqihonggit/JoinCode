namespace Hands.Tests.ToolHandlers;

/// <summary>
/// 行号前缀格式单元测试。
/// 验证 AddLineNumbers 的紧凑(\t)/宽(→)两种输出格式，以及 StripLineNumberPrefixes 的逆剥离。
/// 对齐 TS: addLineNumbers / stripLineNumberPrefix。
/// </summary>
public class LineNumberPrefixTests
{
    [Fact]
    public void AddLineNumbers_Compact_UsesTabSeparator()
    {
        var result = FileToolHandlers.AddLineNumbers("a\nb", 1, compact: true);
        result.Should().Be($"1\ta{Environment.NewLine}2\tb{Environment.NewLine}");
    }

    [Fact]
    public void AddLineNumbers_Wide_UsesArrowSeparator()
    {
        var result = FileToolHandlers.AddLineNumbers("a\nb", 1, compact: false);
        result.Should().Be($"     1\u2192a{Environment.NewLine}     2\u2192b{Environment.NewLine}");
    }

    [Fact]
    public void AddLineNumbers_EmptyContent_ReturnsEmpty()
    {
        FileToolHandlers.AddLineNumbers("", 1, compact: true).Should().BeEmpty();
        FileToolHandlers.AddLineNumbers("", 1, compact: false).Should().BeEmpty();
    }

    [Fact]
    public void AddLineNumbers_WithStartLine_PreservesOffset()
    {
        var result = FileToolHandlers.AddLineNumbers("a", 5, compact: true);
        result.Should().Be($"5\ta{Environment.NewLine}");
    }

    [Fact]
    public void AddLineNumbers_Wide_PadWidthAtLeastSix()
    {
        var result = FileToolHandlers.AddLineNumbers("a", 1, compact: false);
        result.Should().Be($"     1\u2192a{Environment.NewLine}");
    }

    [Fact]
    public void StripLineNumberPrefixes_CompactPrefix_Strips()
    {
        FileEditor.StripLineNumberPrefixes("1\ta\n2\tb").Should().Be("a\nb");
    }

    [Fact]
    public void StripLineNumberPrefixes_WidePrefix_Strips()
    {
        FileEditor.StripLineNumberPrefixes("     1\u2192a\n     2\u2192b").Should().Be("a\nb");
    }

    [Fact]
    public void StripLineNumberPrefixes_NoPrefix_ReturnsOriginal()
    {
        FileEditor.StripLineNumberPrefixes("a\nb").Should().Be("a\nb");
    }

    [Fact]
    public void StripLineNumberPrefixes_PartialPrefix_StripsOnlyPrefixedLines()
    {
        FileEditor.StripLineNumberPrefixes("1\ta\nb").Should().Be("a\nb");
    }

    [Fact]
    public void StripLineNumberPrefixes_Empty_ReturnsEmpty()
    {
        FileEditor.StripLineNumberPrefixes("").Should().Be("");
    }

    [Fact]
    public void StripLineNumberPrefixes_CodeWithTabIndent_NotStripped()
    {
        // 制表符缩进的代码行不以"数字+分隔符"开头，不应被误剥离
        FileEditor.StripLineNumberPrefixes("\tindented code").Should().Be("\tindented code");
    }
}
