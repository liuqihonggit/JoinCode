namespace Host.Tests.Adapters;

/// <summary>
/// TASK138 — SDK 流式输入（<c>--input-format stream-json</c>）的行解析与选项判定。
/// <para>判据来源：Claude Agent SDK 的 stdio 形态 —— 每行一个 JSON 对象，<c>type=user</c> 的
/// <c>message.content</c> 可以是字符串或内容块数组；非 user 类型本轮不消费，只诊断。</para>
/// </summary>
public sealed class SdkStreamInputParserTests {
    [Fact]
    public void StringContent_YieldsUserText() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret(
            """{"type":"user","message":{"role":"user","content":"你好"}}""");

        parsed.Text.Should().Be("你好");
        parsed.Diagnostic.Should().BeNull();
    }

    [Fact]
    public void BlockArrayContent_JoinsTextBlocksAndIgnoresOthers() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret(
            """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"甲"},{"type":"image","source":{}},{"type":"text","text":"乙"}]}}""");

        parsed.Text.Should().Be("甲乙");
    }

    [Fact]
    public void NonUserType_IsDiagnosedNotFatal() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret("""{"type":"control_request","request_id":"r1"}""");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().Contain("仅支持");
    }

    [Fact]
    public void BlankLine_IsSilentlyIgnored() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret("   ");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().BeNull("空行不是错误，循环里静默跳过");
    }

    [Fact]
    public void MalformedJson_IsDiagnosedWithShapeHint() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret("{not json");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().Contain("无法解析");
        parsed.Diagnostic.Should().Contain("message.content", "诊断要诱导出正确形状，否则客户端只能瞎试");
    }

    [Fact]
    public void EmptyText_IsDiagnosed() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret(
            """{"type":"user","message":{"role":"user","content":"   "}}""");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().Contain("为空");
    }

    [Fact]
    public void MissingContent_IsDiagnosed() {
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret("""{"type":"user","message":{"role":"user"}}""");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().Contain("缺 content");
    }

    [Fact]
    public void StreamJsonInput_ImpliesNonInteractiveAndNdjsonOutput() {
        var options = new CommandLineOptions { InputFormat = "stream-json", OutputFormat = "stream-json" };

        options.IsStreamJsonInputMode.Should().BeTrue();
        options.IsNonInteractiveMode.Should().BeTrue("流式输入由 stdin 驱动，不依赖 -p 提示词");
        options.IsNdjsonMode.Should().BeTrue("SDK 方言 stream-json 与 ndjson 同义");
        options.IsStructuredOutputMode.Should().BeTrue("否则事件会以人类可读文本污染协议流");
    }
}
