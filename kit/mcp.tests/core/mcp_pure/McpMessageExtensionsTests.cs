namespace Mcp.Tests;

/// <summary>
/// McpMessageExtensions 单元测试 — 验证 FromJson 4路分派(request/notification/response + 非法)
/// </summary>
public sealed class McpMessageExtensionsTests {
    [Fact]
    public void FromJson_RequestWithMethodAndId_ReturnsRequest() {
        var json = """{"jsonrpc":"2.0","method":"tools/list","id":1}""";
        var message = McpMessageExtensions.FromJson(json);
        message.Should().BeOfType<JsonRpcRequest>();
        var request = (JsonRpcRequest)message;
        request.Method.Should().Be("tools/list");
        request.Id.AsNumber.Should().Be(1);
    }

    [Fact]
    public void FromJson_RequestWithStringId_ReturnsRequest() {
        var json = """{"jsonrpc":"2.0","method":"ping","id":"req-abc"}""";
        var message = McpMessageExtensions.FromJson(json);
        message.Should().BeOfType<JsonRpcRequest>();
        var request = (JsonRpcRequest)message;
        request.Method.Should().Be("ping");
        request.Id.AsString.Should().Be("req-abc");
    }

    [Fact]
    public void FromJson_NotificationWithMethodNoId_ReturnsNotification() {
        var json = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
        var message = McpMessageExtensions.FromJson(json);
        message.Should().BeOfType<JsonRpcNotification>();
        var notification = (JsonRpcNotification)message;
        notification.Method.Should().Be("notifications/initialized");
    }

    [Fact]
    public void FromJson_NotificationWithParams_ReturnsNotification() {
        var json = """{"jsonrpc":"2.0","method":"notifications/progress","params":{"progress":50}}""";
        var message = McpMessageExtensions.FromJson(json);
        message.Should().BeOfType<JsonRpcNotification>();
        var notification = (JsonRpcNotification)message;
        notification.Method.Should().Be("notifications/progress");
        notification.Params.HasValue.Should().BeTrue();
    }

    [Fact]
    public void FromJson_ResponseWithIdNoMethod_ReturnsResponse() {
        var json = """{"jsonrpc":"2.0","id":1,"result":{"tools":[]}}""";
        var message = McpMessageExtensions.FromJson(json);
        message.Should().BeOfType<JsonRpcResponse>();
        var response = (JsonRpcResponse)message;
        response.Id.AsNumber.Should().Be(1);
        response.Result.HasValue.Should().BeTrue();
    }

    [Fact]
    public void FromJson_ResponseWithError_ReturnsResponse() {
        var json = """{"jsonrpc":"2.0","id":2,"error":{"code":-32601,"message":"Method not found"}}""";
        var message = McpMessageExtensions.FromJson(json);
        message.Should().BeOfType<JsonRpcResponse>();
        var response = (JsonRpcResponse)message;
        response.Id.AsNumber.Should().Be(2);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32601);
    }

    [Fact]
    public void FromJson_ArrayNotObject_ThrowsJsonException() {
        var json = """[1, 2, 3]""";
        var act = () => McpMessageExtensions.FromJson(json);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void FromJson_InvalidJson_Throws() {
        var act = () => McpMessageExtensions.FromJson("not json {{{");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void FromJson_ObjectWithMethodAndNullId_ReturnsNotification() {
        // id 为 null → hasId = false → notification
        var json = """{"jsonrpc":"2.0","method":"notifications/cancelled","id":null}""";
        var message = McpMessageExtensions.FromJson(json);
        message.Should().BeOfType<JsonRpcNotification>();
    }

    [Fact]
    public void ToJson_RoundTrip_RequestPreservesMethod() {
        var request = new JsonRpcRequest {
            Id = JsonRpcId.FromNumber(42),
            Method = "tools/call"
        };
        var json = request.ToJson();
        var parsed = McpMessageExtensions.FromJson(json);
        parsed.Should().BeOfType<JsonRpcRequest>();
        ((JsonRpcRequest)parsed).Method.Should().Be("tools/call");
    }
}
