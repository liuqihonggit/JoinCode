namespace Hands.Tests.ToolHandlers;

/// <summary>
/// ToolResultTruncator 截断逻辑单元测试
/// 覆盖边界条件: 无换行符硬截断、恰好等于阈值、最小截断、换行符位置分支
/// </summary>
public sealed class ToolResultTruncatorTests
{
    [Fact]
    public void TruncateAtNewline_WithinLimit_ReturnsOriginal()
    {
        var text = "short text";
        var result = ToolResultTruncator.TruncateAtNewline(text, 100);
        result.Should().Be(text);
    }

    [Fact]
    public void TruncateAtNewline_ExactlyAtLimit_ReturnsOriginal()
    {
        var text = new string('a', 100);
        var result = ToolResultTruncator.TruncateAtNewline(text, 100);
        result.Should().Be(text);
    }

    [Fact]
    public void TruncateAtNewline_OneOverLimit_Truncates()
    {
        var text = new string('a', 101);
        var result = ToolResultTruncator.TruncateAtNewline(text, 100);
        result.Should().Contain("Result truncated");
        // 截断后内容 = 100个'a' + 截断提示，比原始101字符长是正常的（提示信息追加）
        result.Should().StartWith(new string('a', 100));
    }

    [Fact]
    public void TruncateAtNewline_NoNewline_HardTruncatesAtLimit()
    {
        // 单行超长内容，没有换行符 → 走硬截断分支
        var text = new string('a', 200);
        var result = ToolResultTruncator.TruncateAtNewline(text, 100);
        result.Should().Contain("Result truncated");
        // 截断后的内容应以100个'a'开头（硬截断）
        result.Should().StartWith(new string('a', 100));
    }

    [Fact]
    public void TruncateAtNewline_NewlineInSecondHalf_TruncatesAtNewline()
    {
        // 换行符在阈值后半段 → 在换行符处截断
        var text = new string('a', 70) + "\n" + new string('b', 50);
        var result = ToolResultTruncator.TruncateAtNewline(text, 100);
        result.Should().Contain("Result truncated");
        // 截断后应以换行符前的内容开头
        result.Should().StartWith(new string('a', 70));
        // 不应包含换行符后的内容
        result.Should().NotContain("b");
    }

    [Fact]
    public void TruncateAtNewline_NewlineInFirstHalf_HardTruncates()
    {
        // 换行符在阈值前半段 → 走硬截断分支
        var text = new string('a', 20) + "\n" + new string('b', 90);
        var result = ToolResultTruncator.TruncateAtNewline(text, 100);
        result.Should().Contain("Result truncated");
        // 硬截断在100字符处，不回退到换行符
        result.Should().StartWith(new string('a', 20) + "\n" + new string('b', 79));
    }

    [Fact]
    public void TruncateAtNewline_MultipleNewlines_TruncatesAtLastInSecondHalf()
    {
        // 多个换行符，最后一个在后半段
        var text = "line1\nline2\nline3\nline4\nline5\nline6";
        var result = ToolResultTruncator.TruncateAtNewline(text, 20);
        result.Should().Contain("Result truncated");
        // 应在某个换行符处截断，不会切断行内容
        result.Should().NotEndWith("\nline");
    }

    [Fact]
    public void BuildWithSizeLimit_WithinLimit_ReturnsSuccessResult()
    {
        var sb = new StringBuilder("small result");
        var result = ToolResultTruncator.BuildWithSizeLimit(sb, 1000);
        result.IsError.Should().BeFalse();
        result.GetTextContent().Should().Be("small result");
    }

    [Fact]
    public void BuildWithSizeLimit_ExceedsLimit_TruncatesResult()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 100; i++)
        {
            sb.AppendLine($"file_{i}.cs");
        }

        var result = ToolResultTruncator.BuildWithSizeLimit(sb, 50);
        result.IsError.Should().BeFalse();
        var text = result.GetTextContent();
        text.Should().Contain("Result truncated");
        text.Length.Should().BeLessThan(sb.Length);
    }

    [Fact]
    public void TruncateAtNewline_EmptyString_ReturnsEmpty()
    {
        var result = ToolResultTruncator.TruncateAtNewline("", 100);
        result.Should().BeEmpty();
    }

    [Fact]
    public void TruncateAtNewline_WindowsLineEndings_TruncatesCorrectly()
    {
        // Windows \r\n 换行符
        var text = "line1\r\nline2\r\nline3\r\nline4";
        var result = ToolResultTruncator.TruncateAtNewline(text, 15);
        result.Should().Contain("Result truncated");
    }
}
