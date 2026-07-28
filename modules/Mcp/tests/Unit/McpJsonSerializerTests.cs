namespace Mcp.Tests;

/// <summary>
/// McpJsonSerializer 单元测试 — 验证 SerializeObject (非泛型) 对 JSON-RPC 消息类型的序列化
/// 修复背景: SerializeObjectInternal 未处理 JsonRpcRequest/Response/Notification，
///   fallthrough 到 value.ToString() 返回类型名字符串，导致 MCP 服务器收到 500 错误
/// </summary>
public class McpJsonSerializerTests
{
    #region JsonRpcRequest

    [Fact]
    public void SerializeObject_JsonRpcRequest_ReturnsValidJson_NotTypeName()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Id = JsonRpcId.FromNumber(1),
            Method = "initialize",
            Params = JsonDocument.Parse("""{"protocolVersion":"2024-11-05"}""").RootElement.Clone()
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(request);

        // Assert — 不是类型名字符串
        Assert.NotEqual("\"McpProtocol.Contracts.JsonRpcRequest\"", json);
        Assert.NotEqual("McpProtocol.Contracts.JsonRpcRequest", json);

        // Assert — 是有效的 JSON 对象
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

        // Assert — 包含 JSON-RPC 必需字段
        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("initialize", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt64());
    }

    [Fact]
    public void SerializeObject_JsonRpcRequest_CanBeDeserialized()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Id = JsonRpcId.FromString("req-abc"),
            Method = "tools/list",
            Params = JsonDocument.Parse("{}").RootElement.Clone()
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(request);
        var deserialized = JsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcRequest);

        // Assert — 往返一致
        Assert.NotNull(deserialized);
        Assert.Equal("tools/list", deserialized!.Method);
        Assert.Equal("req-abc", deserialized.Id.AsString);
        Assert.Equal("2.0", deserialized.JsonRpc);
    }

    [Fact]
    public void SerializeObject_JsonRpcRequest_WithNumberId_SerializesIdAsNumber()
    {
        // Arrange
        var request = new JsonRpcRequest
        {
            Id = JsonRpcId.FromNumber(42),
            Method = "ping"
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(request);

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("id").ValueKind);
        Assert.Equal(42, doc.RootElement.GetProperty("id").GetInt64());
    }

    #endregion

    #region JsonRpcResponse

    [Fact]
    public void SerializeObject_JsonRpcResponse_ReturnsValidJson_NotTypeName()
    {
        // Arrange
        var response = new JsonRpcResponse
        {
            Id = JsonRpcId.FromNumber(1),
            Result = JsonDocument.Parse("""{"protocolVersion":"2024-11-05","serverInfo":{"name":"mock","version":"1.0"}}""").RootElement.Clone()
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(response);

        // Assert — 不是类型名字符串
        Assert.NotEqual("\"McpProtocol.Contracts.JsonRpcResponse\"", json);
        Assert.NotEqual("McpProtocol.Contracts.JsonRpcResponse", json);

        // Assert — 是有效的 JSON 对象
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt64());
        Assert.True(doc.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public void SerializeObject_JsonRpcResponse_WithError_SerializesErrorField()
    {
        // Arrange
        var response = new JsonRpcResponse
        {
            Id = JsonRpcId.FromNumber(2),
            Error = new JsonRpcError { Code = -32601, Message = "Method not found" }
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(response);

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(-32601, error.GetProperty("code").GetInt32());
        Assert.Equal("Method not found", error.GetProperty("message").GetString());
    }

    [Fact]
    public void SerializeObject_JsonRpcResponse_CanBeDeserialized()
    {
        // Arrange
        var response = new JsonRpcResponse
        {
            Id = JsonRpcId.FromNumber(3),
            Result = JsonDocument.Parse("""{"tools":[]}""").RootElement.Clone()
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(response);
        var deserialized = JsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcResponse);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized!.Id.AsNumber);
        Assert.True(deserialized.Result.HasValue);
    }

    #endregion

    #region JsonRpcNotification

    [Fact]
    public void SerializeObject_JsonRpcNotification_ReturnsValidJson_NotTypeName()
    {
        // Arrange
        var notification = new JsonRpcNotification
        {
            Method = "notifications/initialized"
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(notification);

        // Assert — 不是类型名字符串
        Assert.NotEqual("\"McpProtocol.Contracts.JsonRpcNotification\"", json);
        Assert.NotEqual("McpProtocol.Contracts.JsonRpcNotification", json);

        // Assert — 是有效的 JSON 对象
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("notifications/initialized", doc.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public void SerializeObject_JsonRpcNotification_CanBeDeserialized()
    {
        // Arrange
        var notification = new JsonRpcNotification
        {
            Method = "notifications/cancelled",
            Params = JsonDocument.Parse("""{"requestId":"req-1","reason":"timeout"}""").RootElement.Clone()
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(notification);
        var deserialized = JsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcNotification);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("notifications/cancelled", deserialized!.Method);
        Assert.True(deserialized.Params.HasValue);
    }

    #endregion

    #region 通过基类 JsonRpcMessage 测试 (模拟 ToJson 调用路径)

    [Fact]
    public void SerializeObject_JsonRpcMessage_AsRequest_ReturnsValidJson()
    {
        // Arrange — 模拟 HttpTransport.SendMessageAsync 的调用路径:
        // SendMessageAsync(JsonRpcMessage) -> message.ToJson() -> SerializeObject(message)
        JsonRpcMessage message = new JsonRpcRequest
        {
            Id = JsonRpcId.FromNumber(10),
            Method = "tools/call",
            Params = JsonDocument.Parse("""{"name":"echo","arguments":{"message":"hi"}}""").RootElement.Clone()
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(message);

        // Assert — 必须是有效的 JSON-RPC 请求，不是类型名
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal("tools/call", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal(10, doc.RootElement.GetProperty("id").GetInt64());
        Assert.True(doc.RootElement.TryGetProperty("params", out var paramsProp));
        Assert.Equal("echo", paramsProp.GetProperty("name").GetString());
    }

    [Fact]
    public void SerializeObject_JsonRpcMessage_AsResponse_ReturnsValidJson()
    {
        // Arrange
        JsonRpcMessage message = new JsonRpcResponse
        {
            Id = JsonRpcId.FromNumber(11),
            Result = JsonDocument.Parse("""{"content":[{"type":"text","text":"Echo: hi"}]}""").RootElement.Clone()
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(message);

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal(11, doc.RootElement.GetProperty("id").GetInt64());
        Assert.True(doc.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public void SerializeObject_JsonRpcMessage_AsNotification_ReturnsValidJson()
    {
        // Arrange
        JsonRpcMessage message = new JsonRpcNotification
        {
            Method = "notifications/progress",
            Params = JsonDocument.Parse("""{"progressToken":"tok-1","progress":50}""").RootElement.Clone()
        };

        // Act
        var json = McpJsonSerializer.SerializeObject(message);

        // Assert
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal("notifications/progress", doc.RootElement.GetProperty("method").GetString());
    }

    #endregion

    #region 回归测试 — 确保其他类型仍正常工作

    [Fact]
    public void SerializeObject_String_ReturnsJsonString()
    {
        var json = McpJsonSerializer.SerializeObject("hello");
        Assert.Equal("\"hello\"", json);
    }

    [Fact]
    public void SerializeObject_Null_ReturnsNullLiteral()
    {
        var json = McpJsonSerializer.SerializeObject(null!);
        Assert.Equal("null", json);
    }

    [Fact]
    public void SerializeObject_Int_ReturnsNumber()
    {
        object value = 42;
        var json = McpJsonSerializer.SerializeObject(value);
        Assert.Equal("42", json);
    }

    [Fact]
    public void SerializeObject_Bool_ReturnsLowercase()
    {
        object value = true;
        var json = McpJsonSerializer.SerializeObject(value);
        Assert.Equal("true", json);
    }

    #endregion
}
