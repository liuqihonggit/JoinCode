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

    [Fact]
    public void Parse_ModelCommandWithArgument_ReturnsArgumentMode()
    {
        var result = SlashCommandParser.Parse("/model gp", 9);
        result.ShouldComplete.Should().BeTrue();
        result.Mode.Should().Be(SlashCompletionMode.Argument);
        result.CommandName.Should().Be("/model");
        result.ArgumentPrefix.Should().Be("gp");
        result.ArgumentStart.Should().Be(7);
    }

    [Fact]
    public void Parse_ModelCommandSpaceOnly_ReturnsArgumentModeEmptyPrefix()
    {
        var result = SlashCommandParser.Parse("/model ", 7);
        result.ShouldComplete.Should().BeTrue();
        result.Mode.Should().Be(SlashCompletionMode.Argument);
        result.CommandName.Should().Be("/model");
        result.ArgumentPrefix.Should().Be("");
        result.ArgumentStart.Should().Be(7);
    }

    [Fact]
    public void Parse_ThemeCommandWithArgument_ReturnsArgumentMode()
    {
        var result = SlashCommandParser.Parse("/theme da", 9);
        result.ShouldComplete.Should().BeTrue();
        result.Mode.Should().Be(SlashCompletionMode.Argument);
        result.CommandName.Should().Be("/theme");
        result.ArgumentPrefix.Should().Be("da");
    }

    [Fact]
    public void Parse_AtTrigger_ReturnsFileMode()
    {
        var result = SlashCommandParser.Parse("@src", 4);
        result.ShouldComplete.Should().BeTrue();
        result.Mode.Should().Be(SlashCompletionMode.File);
        result.TriggerChar.Should().Be('@');
        result.Prefix.Should().Be("src");
        result.SlashIndex.Should().Be(0);
    }

    [Fact]
    public void Parse_HashTrigger_ReturnsToolMode()
    {
        var result = SlashCommandParser.Parse("#Read", 5);
        result.ShouldComplete.Should().BeTrue();
        result.Mode.Should().Be(SlashCompletionMode.Tool);
        result.TriggerChar.Should().Be('#');
        result.Prefix.Should().Be("Read");
        result.SlashIndex.Should().Be(0);
    }

    [Fact]
    public void Parse_AtWithSpace_Terminates()
    {
        var result = SlashCommandParser.Parse("@src file", 9);
        result.ShouldComplete.Should().BeFalse();
    }

    [Fact]
    public void Parse_NearestTriggerWins_AtOverSlash()
    {
        var result = SlashCommandParser.Parse("/model @src", 11);
        result.ShouldComplete.Should().BeTrue();
        result.Mode.Should().Be(SlashCompletionMode.File);
        result.Prefix.Should().Be("src");
    }

    [Fact]
    public void Parse_ArgumentWithSpace_Terminates()
    {
        var result = SlashCommandParser.Parse("/model gpt 4o", 13);
        result.ShouldComplete.Should().BeFalse();
    }
}
