
namespace Bridge.Tests;

/// <summary>
/// BridgeMessageSerialization 单元测试
/// 测试各类 BridgeMessage 的 JSON 序列化与反序列化
/// </summary>
public sealed class BridgeMessageSerializationTests
{
    [Fact]
    public void ToJson_InitializeRequest_ReturnsExpectedType()
    {
        var message = new InitializeRequest
        {
            Id = "init-1",
            ClientInfo = new ClientInfo { Name = "test", Version = "1.0" },
            Capabilities = new ClientCapabilities()
        };

        var json = message.ToJson();

        json.Should().Contain("\"type\":\"initialize\"");
        json.Should().Contain("\"id\":\"init-1\"");
    }

    [Fact]
    public void ToJson_And_FromJson_InitializeRequest_RoundTrip()
    {
        var message = new InitializeRequest
        {
            Id = "init-1",
            ProtocolVersion = "1.0",
            ClientInfo = new ClientInfo { Name = "test", Version = "1.0" },
            Capabilities = new ClientCapabilities()
        };

        var json = message.ToJson();
        var deserialized = BridgeMessageSerialization.FromJson(json);

        deserialized.Should().BeOfType<InitializeRequest>();
        deserialized!.Id.Should().Be("init-1");
    }

    [Fact]
    public void ToJson_ControlRequest_ContainsCommandAndParams()
    {
        var parameters = new Dictionary<string, JsonElement>
        {
            ["action"] = JsonSerializer.SerializeToElement("ping")
        };
        var message = new ControlRequest
        {
            Id = "ctrl-1",
            Command = "ping",
            Params = parameters
        };

        var json = message.ToJson();

        json.Should().Contain("\"type\":\"control_request\"");
        json.Should().Contain("\"command\":\"ping\"");
    }

    [Fact]
    public void FromJson_ControlRequest_PreservesCommand()
    {
        var json = "{\"type\":\"control_request\",\"id\":\"ctrl-1\",\"command\":\"getStatus\",\"params\":{}}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        deserialized.Should().BeOfType<ControlRequest>();
        var control = (ControlRequest)deserialized!;
        control.Command.Should().Be("getStatus");
    }

    [Fact]
    public void FromJson_ToolsListRequest_DeserializesCorrectly()
    {
        var json = "{\"type\":\"tools/list\",\"id\":\"tl-1\"}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        deserialized.Should().BeOfType<ToolsListRequest>();
    }

    [Fact]
    public void ToJson_ToolsListResponse_ContainsToolsArray()
    {
        var message = new ToolsListResponse
        {
            Id = "tlr-1",
            Tools =
            [
                new BridgeToolDefinition { Name = "tool-a", Description = "desc", InputSchema = JsonSerializer.SerializeToElement(new { }) }
            ]
        };

        var json = message.ToJson();

        json.Should().Contain("\"type\":\"tools/list_response\"");
        json.Should().Contain("\"tools\":");
    }

    [Fact]
    public void FromJson_ToolsCallRequest_PreservesToolName()
    {
        var json = "{\"type\":\"tools/call\",\"id\":\"tc-1\",\"tool_name\":\"tool-a\",\"arguments\":{}}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        var call = deserialized.Should().BeOfType<ToolsCallRequest>().Subject;
        call.ToolName.Should().Be("tool-a");
    }

    [Fact]
    public void ToJson_ToolsCallResponse_ContainsSuccess()
    {
        var message = new ToolsCallResponse
        {
            Id = "tcr-1",
            ToolCallId = "tc-1",
            Success = true
        };

        var json = message.ToJson();

        json.Should().Contain("\"type\":\"tools/call_response\"");
        json.Should().Contain("\"success\":true");
    }

    [Fact]
    public void FromJson_SkillExecuteRequest_PreservesSkillName()
    {
        var json = "{\"type\":\"skill/execute\",\"id\":\"se-1\",\"skill_name\":\"skill-a\",\"parameters\":{}}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        var request = deserialized.Should().BeOfType<SkillExecuteRequest>().Subject;
        request.SkillName.Should().Be("skill-a");
    }

    [Fact]
    public void ToJson_SkillExecuteResponse_ContainsError()
    {
        var message = new SkillExecuteResponse
        {
            Id = "ser-1",
            Success = false,
            Error = "failed"
        };

        var json = message.ToJson();

        json.Should().Contain("\"type\":\"skill/execute_response\"");
        json.Should().Contain("\"error\":\"failed\"");
    }

    [Fact]
    public void FromJson_ControlResponse_PreservesSuccessAndRequestId()
    {
        var json = "{\"type\":\"control_response\",\"id\":\"cr-1\",\"request_id\":\"ctrl-1\",\"success\":true}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        var response = deserialized.Should().BeOfType<ControlResponse>().Subject;
        response.RequestId.Should().Be("ctrl-1");
        response.Success.Should().BeTrue();
    }

    [Fact]
    public void ToJson_PingMessage_ReturnsExpectedType()
    {
        var message = new PingMessage { Id = "ping-1" };

        var json = message.ToJson();

        json.Should().Contain("\"type\":\"ping\"");
    }

    [Fact]
    public void FromJson_PongMessage_ReturnsPong()
    {
        var json = "{\"type\":\"pong\",\"id\":\"pong-1\"}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        deserialized.Should().BeOfType<PongMessage>();
    }

    [Fact]
    public void ToJson_ErrorMessage_ContainsCodeAndMessage()
    {
        var message = new ErrorMessage
        {
            Id = "err-1",
            Code = -32600,
            Message = "Invalid request"
        };

        var json = message.ToJson();

        json.Should().Contain("\"type\":\"error\"");
        json.Should().Contain("\"code\":-32600");
    }

    [Fact]
    public void FromJson_NotificationMessage_PreservesLevelAndMessage()
    {
        var json = "{\"type\":\"notification\",\"id\":\"n-1\",\"level\":\"warn\",\"message\":\"hello\"}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        var notification = deserialized.Should().BeOfType<NotificationMessage>().Subject;
        notification.Level.Should().Be("warn");
        notification.Message.Should().Be("hello");
    }

    [Fact]
    public void FromJson_EchoMessage_PreservesOriginalMessageId()
    {
        var json = "{\"type\":\"echo\",\"id\":\"e-1\",\"original_message_id\":\"orig-1\"}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        var echo = deserialized.Should().BeOfType<EchoMessage>().Subject;
        echo.OriginalMessageId.Should().Be("orig-1");
    }

    [Fact]
    public void FromJson_UnknownType_ReturnsNull()
    {
        var json = "{\"type\":\"unknown_type\",\"id\":\"x-1\"}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        deserialized.Should().BeNull();
    }

    [Fact]
    public void FromJson_MissingType_ReturnsNull()
    {
        var json = "{\"id\":\"x-1\"}";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        deserialized.Should().BeNull();
    }

    [Fact]
    public void FromJson_NonObject_ReturnsNull()
    {
        var json = "\"not an object\"";

        var deserialized = BridgeMessageSerialization.FromJson(json);

        deserialized.Should().BeNull();
    }

    [Fact]
    public void FromJson_Null_ReturnsNull()
    {
        var deserialized = BridgeMessageSerialization.FromJson("null");

        deserialized.Should().BeNull();
    }

    [Fact]
    public void ToJson_UnknownMessageType_ThrowsInvalidOperationException()
    {
        var message = new TestBridgeMessage();

        var act = () => message.ToJson();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown message type*");
    }

    private sealed class TestBridgeMessage : BridgeMessage
    {
        public override string Type => "test/unknown";
    }
}
