// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Context;

public sealed class AntmlToolCallParserTests {
    private static readonly AntmlToolCallParser Sut = new();

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void TryParse_AntmlInvoke_ReturnsToolCall() {
        var content = """
            <antml:invoke name="edit">
            <antml:parameter name="filePath">/tmp/a.txt</antml:parameter>
            <antml:parameter name="oldString">old</antml:parameter>
            <antml:parameter name="newString">new</antml:parameter>
            </antml:invoke>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("edit");
        var args = JsonDocument.Parse(result.ToolCalls[0].Arguments).RootElement;
        args.GetProperty("filePath").GetString().Should().Be("/tmp/a.txt");
        args.GetProperty("oldString").GetString().Should().Be("old");
        args.GetProperty("newString").GetString().Should().Be("new");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void TryParse_ToolUseFormat_ReturnsToolCall() {
        var content = """
            <tool_use>
            <name>Bash</name>
            <input>
            <parameter name="command">ls -la</parameter>
            <parameter name="description">查看目录</parameter>
            </input>
            </tool_use>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("Bash");
        var args = JsonDocument.Parse(result.ToolCalls[0].Arguments).RootElement;
        args.GetProperty("command").GetString().Should().Be("ls -la");
        args.GetProperty("description").GetString().Should().Be("查看目录");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void TryParse_FunctionCallsFormat_ReturnsToolCall() {
        var content = """
            <function_calls>
            <invoke name="Read">
            <parameter name="filePath">/tmp/a.txt</parameter>
            </invoke>
            </function_calls>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("Read");
        var args = JsonDocument.Parse(result.ToolCalls[0].Arguments).RootElement;
        args.GetProperty("filePath").GetString().Should().Be("/tmp/a.txt");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void TryParse_MultipleAntmlInvokes_ReturnsAllToolCalls() {
        var content = """
            <antml:invoke name="Read">
            <antml:parameter name="filePath">/tmp/a.txt</antml:parameter>
            </antml:invoke>
            <antml:invoke name="Grep">
            <antml:parameter name="pattern">foo</antml:parameter>
            </antml:invoke>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(2);
        result.ToolCalls[0].Name.Should().Be("Read");
        result.ToolCalls[1].Name.Should().Be("Grep");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void TryParse_AntmlWithSurroundingText_ReturnsToolCallAndRemainingText() {
        var content = """
            我先看一下目录。
            <antml:invoke name="Bash">
            <antml:parameter name="command">ls</antml:parameter>
            </antml:invoke>
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
    public void TryParse_NoXmlTags_ReturnsNull() {
        var content = "这是普通文本，没有工具调用".AsSpan();

        var result = Sut.TryParse(content);

        result.Should().BeNull();
    }

    [Fact]
    public void TryParse_EmptyContent_ReturnsNull() {
        var result = Sut.TryParse("".AsSpan());

        result.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void TryParse_AntmlInvokeWithoutParameters_ReturnsToolCallWithEmptyArgs() {
        var content = """
            <antml:invoke name="ListTools">
            </antml:invoke>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("ListTools");
        result.ToolCalls[0].Arguments.Should().Be("{}");
    }

    [Fact]
    public void TryParse_IsolatedParameterTag_ReturnsNull() {
        var content = """
            <parameter name="newString">0</parameter>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void TryParse_ParameterValueZero_ParsesAsStringZero() {
        var content = """
            <antml:invoke name="edit">
            <antml:parameter name="filePath">/tmp/a.txt</antml:parameter>
            <antml:parameter name="newString">0</antml:parameter>
            </antml:invoke>
            """.AsSpan();

        var result = Sut.TryParse(content);

        result.Should().NotBeNull();
        result!.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0].Name.Should().Be("edit");
        var args = JsonDocument.Parse(result.ToolCalls[0].Arguments).RootElement;
        args.GetProperty("newString").GetString().Should().Be("0");
    }
}
