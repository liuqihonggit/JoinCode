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

    /// <summary>
    /// TASK142-B：控制请求的形状来自基准侧 controlSchemas.ts / structuredIO.ts：
    /// <c>{"type":"control_request","request_id":"...","request":{"subtype":"interrupt"}}</c>。
    /// 它不能被当成"非 user 消息"诊断掉，否则 SDK 客户端的 interrupt 永远打不断回合。
    /// </summary>
    [Fact]
    public void ControlInterrupt_IsRecognizedAsControlLine() {
        var parsed = SdkStreamInputParser.Interpret(
            """{"type":"control_request","request_id":"req-1","request":{"subtype":"interrupt"}}""");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().BeNull();
        parsed.InterruptRequestId.Should().Be("req-1");
    }

    /// <summary>基准侧对 control_response / keep_alive 是静默忽略（本仓不发 control_request，无待处理请求）。</summary>
    [Fact]
    public void ControlResponseAndKeepAlive_AreSilentlyIgnored() {
        SdkStreamInputParser.Interpret("""{"type":"control_response","response":{"subtype":"success","request_id":"r1"}}""")
            .Should().Be(SdkStreamInputLine.Blank);
        SdkStreamInputParser.Interpret("""{"type":"keep_alive"}""")
            .Should().Be(SdkStreamInputLine.Blank);
    }

    /// <summary>未实现的控制子类型必须给出诱导式诊断，不能静默吞掉让客户端以为生效了。</summary>
    [Fact]
    public void UnsupportedControlSubtype_IsDiagnosedWithInductiveHint() {
        var parsed = SdkStreamInputParser.Interpret(
            """{"type":"control_request","request_id":"r2","request":{"subtype":"set_permission_mode","mode":"bypass"}}""");

        parsed.Text.Should().BeNull();
        parsed.InterruptRequestId.Should().BeNull();
        parsed.Diagnostic.Should().Contain("interrupt");
    }

    /// <summary>
    /// TASK142-A：<c>--replay-user-messages</c> 此前只在 JccCliArg 声明、无 CLI 登记。
    /// 现在要有真实消费方（回显用户消息给 bridge 父进程派生标题），先锁参数面。
    /// </summary>
    [Fact]
    public void ReplayUserMessages_IsRegisteredByCliParser() {
        var parsed = CliArgParser.Parse([JccCliArgEnumConstants.ReplayUserMessages]);

        parsed.Error.Should().BeNull();
        parsed.ReplayUserMessages.Should().BeTrue();
        ApplicationBuilder.ParseArgs([JccCliArgEnumConstants.ReplayUserMessages])
            .ReplayUserMessages.Should().BeTrue();
    }

    /// <summary>
    /// TASK142-A：回显必须和基准侧 SDKUserMessage 同形（message.content 为 text 块数组），
    /// 且不能带 isReplay —— 基准 extractUserMessageText 会跳过 isReplay，父进程拿不到第一条真人消息。
    /// </summary>
    [Fact]
    public void ReplayEcho_UsesRealUserMessageShapeWithoutIsReplay() {
        var echo = SdkUserMessage.Create(
            new SdkMessageBody { Role = "user", Content = [new SdkTextBlock { Text = "你好" }] },
            "session-1");
        var json = RelaxedJsonSerializer.Serialize(echo, JoinCode.Cli.Output.CliOutputJsonContext.Default);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("type").GetString().Should().Be("user");
        document.RootElement.GetProperty("message").GetProperty("role").GetString().Should().Be("user");
        document.RootElement.GetProperty("message").GetProperty("content")[0]
            .GetProperty("type").GetString().Should().Be("text");
        document.RootElement.TryGetProperty("isReplay", out _).Should().BeFalse(
            "isReplay=true 的回执会被父进程当成假消息跳过 —— 标题派生链会断");
    }
}
