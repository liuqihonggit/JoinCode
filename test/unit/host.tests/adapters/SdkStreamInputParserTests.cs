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
        var parsed = JoinCode.Cli.Output.SdkStreamInputParser.Interpret(
            """{"type":"assistant","message":{"role":"assistant","content":"我不是输入"}}""");

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
    /// TASK142-B / TASK144：控制请求的形状来自基准侧 controlSchemas.ts / structuredIO.ts：
    /// <c>{"type":"control_request","request_id":"...","request":{"subtype":"interrupt"}}</c>。
    /// 它不能被当成"非 user 消息"诊断掉，否则 SDK 客户端的 interrupt 永远打不断回合。
    /// <para>TASK144 起解析器只**降级**成 SdkControlCommand，不再自己分派 subtype。</para>
    /// </summary>
    [Fact]
    public void ControlInterrupt_IsRecognizedAsControlLine() {
        var parsed = SdkStreamInputParser.Interpret(
            """{"type":"control_request","request_id":"req-1","request":{"subtype":"interrupt"}}""");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().BeNull();
        var control = RequireControl(parsed);
        control.Subtype.Should().Be("interrupt");
        control.RequestId.Should().Be("req-1");
    }

    /// <summary>基准侧对 control_response / keep_alive 是静默忽略（本仓不发 control_request，无待处理请求）。</summary>
    [Fact]
    public void ControlResponseAndKeepAlive_AreSilentlyIgnored() {
        SdkStreamInputParser.Interpret("""{"type":"control_response","response":{"subtype":"success","request_id":"r1"}}""")
            .Should().Be(SdkStreamInputLine.Blank);
        SdkStreamInputParser.Interpret("""{"type":"keep_alive"}""")
            .Should().Be(SdkStreamInputLine.Blank);
    }

    /// <summary>
    /// 未实现的控制子类型：解析器**不判实现**，照常降级成命令 —— 拒绝由控制面统一回 error
    /// 并带回 request_id（基准侧 print.ts 末尾同此），只写 stderr 会让 SDK 客户端等一条永远不来的应答。
    /// </summary>
    [Fact]
    public void UnsupportedControlSubtype_IsDowngradedForControlPlaneToReject() {
        var parsed = SdkStreamInputParser.Interpret(
            """{"type":"control_request","request_id":"r2","request":{"subtype":"set_model","model":"x"}}""");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().BeNull("拒绝是控制面的职责，解析器不预判");
        var control = RequireControl(parsed);
        control.Subtype.Should().Be("set_model");
        control.RequestId.Should().Be("r2");
    }

    /// <summary>
    /// TASK143：<c>set_permission_mode</c> 形状取自基准侧
    /// <c>SDKControlSetPermissionModeRequestSchema</c>（<c>request.mode</c>）。
    /// 它必须成为控制指令而不是被诊断掉 —— 否则 SDK 客户端无法在流式会话里调整权限。
    /// </summary>
    [Fact]
    public void ControlSetPermissionMode_IsRecognizedAsControlLine() {
        var parsed = SdkStreamInputParser.Interpret(
            """{"type":"control_request","request_id":"req-pm","request":{"subtype":"set_permission_mode","mode":"ask"}}""");

        parsed.Text.Should().BeNull();
        parsed.Diagnostic.Should().BeNull();
        var control = RequireControl(parsed);
        control.Subtype.Should().Be("set_permission_mode");
        control.RequestId.Should().Be("req-pm");
        SdkControlPayload.ReadString(control.Payload, "mode").Should().Be("ask");
    }

    /// <summary>
    /// 缺 mode 的控制行同样降级成命令（payload 里没有 mode）；「回 error」由控制面负责 ——
    /// 基准侧 handleSetPermissionMode 对每种不满足都回 <c>control_response.error</c>，
    /// 只写 stderr 会让客户端永久等待这条 request_id。
    /// </summary>
    [Fact]
    public void ControlSetPermissionMode_WithoutMode_KeepsCommandForPlaneToReject() {
        var parsed = SdkStreamInputParser.Interpret(
            """{"type":"control_request","request_id":"req-pm2","request":{"subtype":"set_permission_mode"}}""");

        var control = RequireControl(parsed);
        control.RequestId.Should().Be("req-pm2", "没有 request_id 就无从回 error，有 request_id 就必须交给控制面");
        SdkControlPayload.ReadString(control.Payload, "mode").Should().BeNull();
    }

    /// <summary>取控制命令（无则显式失败，不用 null-forgiving 绕过类型系统 — JCC11003）。</summary>
    private static SdkControlCommand RequireControl(SdkStreamInputLine parsed) =>
        parsed.Control ?? throw new InvalidOperationException("控制行必须在解析阶段降级成 SdkControlCommand");

    /// <summary>
    /// 失败应答必须是基准侧的 error 分支形状：<c>{subtype:"error", request_id, error}</c>，
    /// 而不是把原因塞进 success.response 里让客户端误判成功。
    /// </summary>
    [Fact]
    public void ControlErrorResponse_UsesProtocolErrorBranch() {
        var json = RelaxedJsonSerializer.Serialize(
            SdkStreamControlResponse.Failure("req-pm3", "未知权限模式 'bogus'"),
            JoinCode.Cli.Output.CliOutputJsonContext.Default);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("type").GetString().Should().Be("control_response");
        var body = root.GetProperty("response");
        body.GetProperty("subtype").GetString().Should().Be("error");
        body.GetProperty("request_id").GetString().Should().Be("req-pm3");
        body.GetProperty("error").GetString().Should().Contain("bogus");
        body.TryGetProperty("response", out _).Should().BeFalse("error 分支没有 response 对象");
    }

    /// <summary>
    /// 成功应答把实际生效模式带回（bypass 可能被安全闸拦成别的值）——
    /// 基准侧 response 是开放 record，带 mode 键合法；不带则客户端无从知道真实状态。
    /// </summary>
    [Fact]
    public void ControlSuccessResponse_ReportsEffectiveMode() {
        var json = RelaxedJsonSerializer.Serialize(
            SdkStreamControlResponse.Success("req-pm4", "auto"),
            JoinCode.Cli.Output.CliOutputJsonContext.Default);

        using var document = JsonDocument.Parse(json);
        var body = document.RootElement.GetProperty("response");
        body.GetProperty("subtype").GetString().Should().Be("success");
        body.GetProperty("request_id").GetString().Should().Be("req-pm4");
        body.GetProperty("response").GetProperty("mode").GetString().Should().Be("auto");
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
