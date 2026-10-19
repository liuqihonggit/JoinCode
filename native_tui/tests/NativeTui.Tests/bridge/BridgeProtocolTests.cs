namespace NativeTui.Tests.Bridge;

/// <summary>
/// bridge 进程协议测试 — newline-delimited JSON over stdio（ADR 0141）：
/// 请求/事件 DTO 双向 roundtrip、type 判别、空行跳过、未知 type 诊断（诱导方式）、流终止。
/// </summary>
public sealed class BridgeProtocolTests
{
    [Fact]
    public void WriteRequest_Submit_SerializesWithTypeDiscriminator()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Submit("hello world"));
        Assert.Equal("{\"type\":\"submit\",\"text\":\"hello world\"}\n", writer.ToString());
    }

    [Fact]
    public void WriteRequest_InterruptAndQuit_NoExtraFields()
    {
        var interrupt = new StringWriter();
        BridgeProtocol.WriteRequest(interrupt, new BridgeRequest.Interrupt());
        Assert.Equal("{\"type\":\"interrupt\"}\n", interrupt.ToString());

        var quit = new StringWriter();
        BridgeProtocol.WriteRequest(quit, new BridgeRequest.Quit());
        Assert.Equal("{\"type\":\"quit\"}\n", quit.ToString());
    }

    [Fact]
    public void WriteRequest_SetPermissionMode_CarriesModeField()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.SetPermissionMode("plan"));
        Assert.Equal("{\"type\":\"set_permission_mode\",\"mode\":\"plan\"}\n", writer.ToString());
    }

    [Fact]
    public void ReadEvent_SessionInfo_ParsesEngineHandshake()
    {
        var reader = new StringReader("{\"type\":\"session_info\",\"version\":\"2.1.303\",\"model\":\"glm-4.7\",\"permissionMode\":\"plan\",\"bypassAvailable\":false,\"cwd\":\"D:\\\\engine\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        var info = Assert.IsType<BridgeEvent.SessionInfo>(@event);
        Assert.Equal("2.1.303", info.Version);
        Assert.Equal("glm-4.7", info.Model);
        Assert.Equal("plan", info.PermissionMode);
        Assert.False(info.BypassAvailable);
        Assert.Equal(@"D:\engine", info.Cwd);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void ReadEvent_SessionInfoWithoutModel_ParsesNullModel()
    {
        var reader = new StringReader("{\"type\":\"session_info\",\"version\":\"1.0.0\",\"model\":null,\"permissionMode\":\"auto\",\"bypassAvailable\":true,\"cwd\":\"C:\\\\wd\"}\n");
        BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        var info = Assert.IsType<BridgeEvent.SessionInfo>(@event);
        Assert.Null(info.Model);
        Assert.True(info.BypassAvailable);
    }

    [Fact]
    public void ReadEvent_PermissionModeChanged_CarriesMode()
    {
        var reader = new StringReader("{\"type\":\"permission_mode_changed\",\"mode\":\"unattended\"}\n");
        BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        var changed = Assert.IsType<BridgeEvent.PermissionModeChanged>(@event);
        Assert.Equal("unattended", changed.Mode);
    }

    [Fact]
    public void WriteRequest_AppendsNewlineDelimiter()
    {
        var writer = new StringWriter();
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Submit("x"));
        BridgeProtocol.WriteRequest(writer, new BridgeRequest.Interrupt());
        Assert.Equal("{\"type\":\"submit\",\"text\":\"x\"}\n{\"type\":\"interrupt\"}\n", writer.ToString());
    }

    [Fact]
    public void ReadEvent_TurnStarted_Parses()
    {
        var reader = new StringReader("{\"type\":\"turn_started\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.IsType<BridgeEvent.TurnStarted>(@event);
    }

    [Theory]
    [InlineData("{\"type\":\"thinking_started\"}", typeof(BridgeEvent.ThinkingStarted))]
    [InlineData("{\"type\":\"turn_completed\"}", typeof(BridgeEvent.TurnCompleted))]
    [InlineData("{\"type\":\"turn_interrupted\"}", typeof(BridgeEvent.TurnInterrupted))]
    public void ReadEvent_MarkerEvents_ParseByType(string line, Type expected)
    {
        var reader = new StringReader(line + "\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.IsType(expected, @event);
    }

    [Fact]
    public void ReadEvent_ThinkingEnded_CarriesDuration()
    {
        var reader = new StringReader("{\"type\":\"thinking_ended\",\"durationMs\":5000}\n");
        BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        var ended = Assert.IsType<BridgeEvent.ThinkingEnded>(@event);
        Assert.Equal(5000L, ended.DurationMs);
    }

    [Fact]
    public void ReadEvent_ToolCall_CarriesNameAndArgs()
    {
        var reader = new StringReader("{\"type\":\"tool_call\",\"name\":\"Read\",\"args\":\"src/main.cs\"}\n");
        BridgeProtocol.TryReadEvent(reader, out var @event, out _);
        var call = Assert.IsType<BridgeEvent.ToolCall>(@event);
        Assert.Equal("Read", call.Name);
        Assert.Equal("src/main.cs", call.Args);
    }

    [Fact]
    public void ReadEvent_ToolResultCarriesSummary_TokenDeltaCarriesLength()
    {
        var reader = new StringReader("{\"type\":\"tool_result\",\"summary\":\"41 lines\"}\n{\"type\":\"token_delta\",\"length\":9}\n");
        BridgeProtocol.TryReadEvent(reader, out var first, out _);
        var summary = Assert.IsType<BridgeEvent.ToolResult>(first);
        Assert.Equal("41 lines", summary.Summary);

        BridgeProtocol.TryReadEvent(reader, out var second, out _);
        var delta = Assert.IsType<BridgeEvent.TokenDelta>(second);
        Assert.Equal(9, delta.Length);
    }

    [Fact]
    public void ReadEvent_AssistantMessageCarriesText_ErrorCarriesMessage()
    {
        var reader = new StringReader("{\"type\":\"assistant_message\",\"text\":\"回复内容\"}\n{\"type\":\"error\",\"message\":\"boom\"}\n");
        BridgeProtocol.TryReadEvent(reader, out var first, out _);
        Assert.Equal("回复内容", Assert.IsType<BridgeEvent.AssistantMessage>(first).Text);

        BridgeProtocol.TryReadEvent(reader, out var second, out _);
        Assert.Equal("boom", Assert.IsType<BridgeEvent.Error>(second).Message);
    }

    [Fact]
    public void ReadEvent_EofAtStreamEnd()
    {
        var reader = new StringReader("{\"type\":\"turn_started\"}\n");
        BridgeProtocol.TryReadEvent(reader, out _, out _);
        var result = BridgeProtocol.TryReadEvent(reader, out _, out _);
        Assert.Equal(BridgeReadResult.Eof, result);
    }

    [Fact]
    public void ReadEvent_EmptyLine_SkippedWithoutDiagnostic()
    {
        var reader = new StringReader("\n\n{\"type\":\"turn_completed\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        Assert.IsType<BridgeEvent.TurnCompleted>(@event);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void ReadEvent_UnknownType_SkippedWithInductiveDiagnostic()
    {
        var reader = new StringReader("{\"type\":\"warp_drive\"}\n{\"type\":\"turn_completed\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var @event, out var diagnostic);
        Assert.Equal(BridgeReadResult.Skipped, result);
        Assert.Null(@event);
        // 诊断必须解释：是什么触发 + 为什么跳过 + 支持的合法类型清单（诱导方式）
        Assert.NotNull(diagnostic);
        Assert.Contains("warp_drive", diagnostic);
        Assert.Contains("turn_started", diagnostic);

        // 跳过后继续读后续合法事件
        var next = BridgeProtocol.TryReadEvent(reader, out var resumed, out _);
        Assert.Equal(BridgeReadResult.Event, next);
        Assert.IsType<BridgeEvent.TurnCompleted>(resumed);
    }

    [Fact]
    public void ReadEvent_InvalidJson_SkippedWithDiagnostic()
    {
        var reader = new StringReader("not json at all\n");
        var result = BridgeProtocol.TryReadEvent(reader, out _, out var diagnostic);
        Assert.Equal(BridgeReadResult.Skipped, result);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public void ReadRequest_Submit_ParsesText()
    {
        var reader = new StringReader("{\"type\":\"submit\",\"text\":\"你好 world\"}\n");
        var result = BridgeProtocol.TryReadRequest(reader, out var request, out var diagnostic);
        Assert.Equal(BridgeReadResult.Event, result);
        var submit = Assert.IsType<BridgeRequest.Submit>(request);
        Assert.Equal("你好 world", submit.Text);
        Assert.Null(diagnostic);
    }

    [Fact]
    public void ReadRequest_InterruptAndQuit_ParseByType()
    {
        var reader = new StringReader("{\"type\":\"interrupt\"}\n{\"type\":\"quit\"}\n");
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out var first, out _));
        Assert.IsType<BridgeRequest.Interrupt>(first);
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out var second, out _));
        Assert.IsType<BridgeRequest.Quit>(second);
    }

    [Fact]
    public void ReadRequest_UnknownType_SkippedWithInductiveDiagnostic()
    {
        var reader = new StringReader("{\"type\":\"teleport\"}\n{\"type\":\"quit\"}\n");
        var result = BridgeProtocol.TryReadRequest(reader, out var request, out var diagnostic);
        Assert.Equal(BridgeReadResult.Skipped, result);
        Assert.Null(request);
        Assert.NotNull(diagnostic);
        Assert.Contains("teleport", diagnostic);
        Assert.Contains("submit", diagnostic);

        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out var resumed, out _));
        Assert.IsType<BridgeRequest.Quit>(resumed);
    }

    [Fact]
    public void ReadRequest_EmptyLineSkipped_EofAtStreamEnd()
    {
        var reader = new StringReader("\n{\"type\":\"interrupt\"}\n");
        Assert.Equal(BridgeReadResult.Event, BridgeProtocol.TryReadRequest(reader, out _, out _));
        Assert.Equal(BridgeReadResult.Eof, BridgeProtocol.TryReadRequest(reader, out _, out _));
    }

    [Fact]
    public void Roundtrip_AllEvents_SurviveSerializeDeserialize()
    {
        BridgeEvent[] events =
        [
            new BridgeEvent.SessionInfo("2.1.303", null, "auto", false, @"D:\engine"),
            new BridgeEvent.PermissionModeChanged("bypass"),
            new BridgeEvent.TurnStarted(),
            new BridgeEvent.ThinkingStarted(),
            new BridgeEvent.ThinkingEnded(4321),
            new BridgeEvent.ToolCall("Bash", "ls -la"),
            new BridgeEvent.ToolResult("3 files"),
            new BridgeEvent.TokenDelta(12),
            new BridgeEvent.AssistantMessage("完整回复 — 包含中文与 \"引号\""),
            new BridgeEvent.TurnCompleted(),
            new BridgeEvent.TurnInterrupted(),
            new BridgeEvent.Error("出错了：参数缺失"),
        ];

        var writer = new StringWriter();
        foreach (var bridgeEvent in events)
        {
            writer.WriteLine(BridgeProtocol.SerializeEvent(bridgeEvent));
        }

        var reader = new StringReader(writer.ToString());
        foreach (var expected in events)
        {
            var result = BridgeProtocol.TryReadEvent(reader, out var actual, out var diagnostic);
            Assert.Equal(BridgeReadResult.Event, result);
            Assert.NotNull(actual);
            Assert.Equal(expected, actual);
            Assert.Null(diagnostic);
        }
    }
}
