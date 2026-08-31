namespace Core.Bridge.Models;

/// <summary>
/// BridgeMessage 序列化扩展 — 使用 BridgeJsonContext 实现 AOT 兼容序列化
/// </summary>
public static class BridgeMessageSerialization
{
    public static string ToJson(this BridgeMessage message)
    {
        return message switch
        {
            InitializeRequest r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.InitializeRequest),
            InitializeResponse r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.InitializeResponse),
            ToolsListRequest r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.ToolsListRequest),
            ToolsListResponse r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.ToolsListResponse),
            ToolsCallRequest r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.ToolsCallRequest),
            ToolsCallResponse r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.ToolsCallResponse),
            SkillExecuteRequest r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.SkillExecuteRequest),
            SkillExecuteResponse r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.SkillExecuteResponse),
            ControlRequest r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.ControlRequest),
            ControlResponse r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.ControlResponse),
            PingMessage r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.PingMessage),
            PongMessage r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.PongMessage),
            ErrorMessage r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.ErrorMessage),
            NotificationMessage r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.NotificationMessage),
            EchoMessage r => JsonSerializer.Serialize(r, BridgeJsonContext.Default.EchoMessage),
            _ => throw new InvalidOperationException($"Unknown message type: {message.GetType().Name}")
        };
    }

    public static BridgeMessage? FromJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj)
            return null;

        if (!obj.TryGetPropertyValue("type", out var typeNode))
            return null;

        var type = typeNode?.GetValue<string>();
        return type switch
        {
            "initialize" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.InitializeRequest),
            "initialize_response" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.InitializeResponse),
            "tools/list" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.ToolsListRequest),
            "tools/list_response" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.ToolsListResponse),
            "tools/call" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.ToolsCallRequest),
            "tools/call_response" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.ToolsCallResponse),
            "skill/execute" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.SkillExecuteRequest),
            "skill/execute_response" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.SkillExecuteResponse),
            "control_request" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.ControlRequest),
            "control_response" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.ControlResponse),
            "ping" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.PingMessage),
            "pong" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.PongMessage),
            "error" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.ErrorMessage),
            "notification" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.NotificationMessage),
            "echo" => RelaxedJsonSerializer.Deserialize(json, BridgeJsonContext.Default.EchoMessage),
            _ => null
        };
    }
}
