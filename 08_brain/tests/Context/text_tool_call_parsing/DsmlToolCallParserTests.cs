namespace Core.Context;

public sealed class DsmlToolCallParserTests
{
    private static readonly DsmlToolCallParser Sut = new();

    [Fact]
    public void TryParse_ValidSingleInvoke_ReturnsOneToolCall()
    {
        var content = """
            <｜DSML｜tool_calls>
            <｜DSML｜invoke name="Bash">
            <｜DSML｜parameter name="command" string="true">ls -la</｜DSML｜parameter>
            <｜DSML｜parameter name="description" string="true">查看目录</｜DSML｜parameter>
            </｜DSML｜invoke>
            </｜DSML｜tool_calls>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("Bash");
        var args = JsonDocument.Parse(result.ToolCalls[0].Arguments).RootElement;
        args.GetProperty("command").GetString().Should().Be("ls -la");
        args.GetProperty("description").GetString().Should().Be("查看目录");
    }

    [Fact]
    public void TryParse_NoDsmlMarker_ReturnsNull()
    {
        var content = "这是普通文本，没有工具调用".AsSpan();

        var result = Sut.TryParse(content);

        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_MultipleInvokes_ReturnsAllToolCalls()
    {
        var content = """
            <｜DSML｜tool_calls>
            <｜DSML｜invoke name="Read">
            <｜DSML｜parameter name="filePath" string="true">/tmp/a.txt</｜DSML｜parameter>
            </｜DSML｜invoke>
            <｜DSML｜invoke name="Grep">
            <｜DSML｜parameter name="pattern" string="true">foo</｜DSML｜parameter>
            </｜DSML｜invoke>
            </｜DSML｜tool_calls>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(2);
        result.ToolCalls[0].Name.Should().Be("Read");
        result.ToolCalls[1].Name.Should().Be("Grep");
    }

    [Fact]
    public void TryParse_DsmlWithSurroundingText_ReturnsToolCallAndRemainingText()
    {
        var content = """
            我先看一下目录。
            <｜DSML｜tool_calls>
            <｜DSML｜invoke name="Bash">
            <｜DSML｜parameter name="command" string="true">ls</｜DSML｜parameter>
            </｜DSML｜invoke>
            </｜DSML｜tool_calls>
            完成。
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("Bash");
        result.RemainingText.Should().NotBeNull();
        result.RemainingText.Should().Contain("我先看一下目录");
        result.RemainingText.Should().Contain("完成");
    }

    [Fact]
    public void TryParse_EmptyContent_ReturnsNull()
    {
        var result = Sut.TryParse("".AsSpan());

        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_InvokeWithoutParameters_ReturnsToolCallWithEmptyArgs()
    {
        var content = """
            <｜DSML｜tool_calls>
            <｜DSML｜invoke name="ListTools">
            </｜DSML｜invoke>
            </｜DSML｜tool_calls>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("ListTools");
        result.ToolCalls[0].Arguments.Should().Be("{}");
    }
}
