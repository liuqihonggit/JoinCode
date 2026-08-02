namespace Hands.Tests.Shell;

[Trait("Category", "Unit")]
public class ClaudeCodeHintExtractorTests
{
    [Fact]
    public void Extract_NoHintTag_ReturnsEmptyAndOriginalOutput()
    {
        var result = ClaudeCodeHintExtractor.Extract("Hello world", "echo");
        result.Hints.Should().BeEmpty();
        result.StrippedOutput.Should().Be("Hello world");
    }

    [Fact]
    public void Extract_ValidPluginHint_ReturnsHintAndStripsTag()
    {
        var output = "some output\n<claude-code-hint v=\"1\" type=\"plugin\" value=\"my-plugin@marketplace\" />\nmore output";
        var result = ClaudeCodeHintExtractor.Extract(output, "mycli arg1");

        result.Hints.Should().HaveCount(1);
        result.Hints[0].V.Should().Be(1);
        result.Hints[0].Type.Should().Be("plugin");
        result.Hints[0].Value.Should().Be("my-plugin@marketplace");
        result.Hints[0].SourceCommand.Should().Be("mycli");
        result.StrippedOutput.Should().NotContain("<claude-code-hint");
        result.StrippedOutput.Should().Contain("some output");
        result.StrippedOutput.Should().Contain("more output");
    }

    [Fact]
    public void Extract_UnsupportedVersion_DropsHint()
    {
        var output = "<claude-code-hint v=\"99\" type=\"plugin\" value=\"test\" />";
        var result = ClaudeCodeHintExtractor.Extract(output, "cmd");

        result.Hints.Should().BeEmpty();
        result.StrippedOutput.Should().NotContain("<claude-code-hint");
    }

    [Fact]
    public void Extract_UnsupportedType_DropsHint()
    {
        var output = "<claude-code-hint v=\"1\" type=\"unknown\" value=\"test\" />";
        var result = ClaudeCodeHintExtractor.Extract(output, "cmd");

        result.Hints.Should().BeEmpty();
        result.StrippedOutput.Should().NotContain("<claude-code-hint");
    }

    [Fact]
    public void Extract_EmptyValue_DropsHint()
    {
        var output = "<claude-code-hint v=\"1\" type=\"plugin\" value=\"\" />";
        var result = ClaudeCodeHintExtractor.Extract(output, "cmd");

        result.Hints.Should().BeEmpty();
    }

    [Fact]
    public void Extract_MultipleHints_ReturnsAll()
    {
        var output = "<claude-code-hint v=\"1\" type=\"plugin\" value=\"plugin-a@market\" />\n<claude-code-hint v=\"1\" type=\"plugin\" value=\"plugin-b@market\" />";
        var result = ClaudeCodeHintExtractor.Extract(output, "cmd");

        result.Hints.Should().HaveCount(2);
        result.Hints[0].Value.Should().Be("plugin-a@market");
        result.Hints[1].Value.Should().Be("plugin-b@market");
    }

    [Fact]
    public void Extract_QuotedAttributes_ParsedCorrectly()
    {
        var output = "<claude-code-hint v=\"1\" type=\"plugin\" value=\"my plugin@market\" />";
        var result = ClaudeCodeHintExtractor.Extract(output, "cmd");

        result.Hints.Should().HaveCount(1);
        result.Hints[0].Value.Should().Be("my plugin@market");
    }

    [Fact]
    public void Extract_HintBuriedInLargerLine_Ignored()
    {
        var output = "log: <claude-code-hint v=\"1\" type=\"plugin\" value=\"test\" /> end";
        var result = ClaudeCodeHintExtractor.Extract(output, "cmd");

        result.Hints.Should().BeEmpty();
        result.StrippedOutput.Should().Be(output);
    }

    [Fact]
    public void Extract_LeadingWhitespaceOnLine_Accepted()
    {
        var output = "  <claude-code-hint v=\"1\" type=\"plugin\" value=\"test\" />";
        var result = ClaudeCodeHintExtractor.Extract(output, "cmd");

        result.Hints.Should().HaveCount(1);
    }

    [Fact]
    public void Extract_SourceCommand_ExtractsFirstToken()
    {
        var output = "<claude-code-hint v=\"1\" type=\"plugin\" value=\"test\" />";
        var result = ClaudeCodeHintExtractor.Extract(output, "/usr/bin/mycli --flag arg");

        result.Hints.Should().HaveCount(1);
        result.Hints[0].SourceCommand.Should().Be("/usr/bin/mycli");
    }

    [Fact]
    public void Extract_CollapsesExcessiveBlankLines()
    {
        var output = "line1\n<claude-code-hint v=\"1\" type=\"plugin\" value=\"test\" />\n\n\n\nline2";
        var result = ClaudeCodeHintExtractor.Extract(output, "cmd");

        result.StrippedOutput.Should().NotContain("<claude-code-hint");
        result.StrippedOutput.Should().NotContain("\n\n\n");
    }
}
