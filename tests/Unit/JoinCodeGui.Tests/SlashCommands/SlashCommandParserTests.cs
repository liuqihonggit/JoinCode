using FluentAssertions;

using JoinCode.Gui.SlashCommands;

namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// SlashCommandParser 单元测试 — 验证光标解析、空格终止、多行、连续 // 等场景。
/// </summary>
public class SlashCommandParserTests
{
    [Fact]
    public void Parse_BasicPrefix_ReturnsPrefix()
    {
        var result = SlashCommandParser.Parse("/ap", 3);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/ap");
        result.SlashIndex.Should().Be(0);
        result.PrefixEnd.Should().Be(3);
    }

    [Fact]
    public void Parse_SlashInMiddle_TakesNearestSlashBeforeCursor()
    {
        var result = SlashCommandParser.Parse("hello /ap", 9);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/ap");
        result.SlashIndex.Should().Be(6);
    }

    [Fact]
    public void Parse_SpaceAfterCommand_TerminatesCompletion()
    {
        var result = SlashCommandParser.Parse("/clear ", 7);
        result.ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_SpaceInPrefix_TerminatesCompletion()
    {
        var result = SlashCommandParser.Parse("/ap world", 9);
        result.ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_ConsecutiveSlashes_TakesLastSlash()
    {
        var result = SlashCommandParser.Parse("//ap", 4);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/ap");
        result.SlashIndex.Should().Be(1);
    }

    [Fact]
    public void Parse_Multiline_SlashOnAnyLine_Triggers()
    {
        var result = SlashCommandParser.Parse("hello\n/ap", 9);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/ap");
        result.SlashIndex.Should().Be(6);
    }

    [Fact]
    public void Parse_OnlySlash_ReturnsSlashPrefix()
    {
        var result = SlashCommandParser.Parse("/", 1);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/");
    }

    [Fact]
    public void Parse_NoSlash_ReturnsNone()
    {
        var result = SlashCommandParser.Parse("hello", 5);
        result.ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_CursorBeforeSlash_ReturnsNone()
    {
        var result = SlashCommandParser.Parse("/ap", 0);
        result.ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_CursorImmediatelyAfterSlash_ReturnsSlashPrefix()
    {
        var result = SlashCommandParser.Parse("/ap", 1);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/");
    }

    [Fact]
    public void Parse_SlashFollowedBySpace_Terminates()
    {
        var result = SlashCommandParser.Parse("/ ", 2);
        result.ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_PastedTextWithSlash_Triggers()
    {
        var result = SlashCommandParser.Parse("paste /ap", 9);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/ap");
    }

    [Fact]
    public void Parse_CursorMoved_ReparsesNearestSlash()
    {
        var result = SlashCommandParser.Parse("/ap", 2);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/a");
    }

    [Fact]
    public void Parse_EmptyText_ReturnsNone()
    {
        SlashCommandParser.Parse("", 0).ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_NegativeCursor_ReturnsNone()
    {
        SlashCommandParser.Parse("/ap", -1).ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_CursorBeyondLength_ReturnsNone()
    {
        SlashCommandParser.Parse("/ap", 10).ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_SlashAfterSpace_OnSameLine_Triggers()
    {
        var result = SlashCommandParser.Parse("/clear /mo", 10);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/mo");
        result.SlashIndex.Should().Be(7);
    }

    [Fact]
    public void Parse_Multiline_SlashOnSecondLine_WithSpaceAfter_Terminates()
    {
        var result = SlashCommandParser.Parse("hello\n/clear ", 13);
        result.ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_Multiline_SlashInMiddleLine_Triggers()
    {
        var result = SlashCommandParser.Parse("line1\n/cmd\nline3", 10);
        result.ShouldComplete.Should().BeTrue();
        result.Prefix.Should().Be("/cmd");
        result.SlashIndex.Should().Be(6);
    }

    [Fact]
    public void Parse_Multiline_CursorOnDifferentLine_NoTrigger()
    {
        var result = SlashCommandParser.Parse("line1\n/cmd\nline3", 12);
        result.ShouldComplete.Should().BeFalse();
    }
}
