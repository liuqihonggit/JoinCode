
namespace McpClient;

internal static class McpMessageExtensions {
    /// <summary>将 JSON-RPC 消息序列化为 JSON 字符串。</summary>
    public static string ToJson(this JsonRpcMessage message) {
        return McpJsonSerializer.SerializeObject(message);
    }

    /// <summary>将 JSON 字符串反序列化为 JSON-RPC 消息。</summary>
    public static JsonRpcMessage FromJson(string json) {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj)
            throw new JsonException("Invalid JSON-RPC message");

        var hasId = obj.TryGetPropertyValue("id", out var idNode) && idNode is not null;
        var hasMethod = obj.ContainsKey("method");

        if (hasMethod) {
            if (hasId) {
                var request = McpJsonSerializer.DeserializeJsonRpcRequest(json);
                return request ?? throw new JsonException("Cannot parse JSON-RPC request");
            } else {
                var notification = McpJsonSerializer.DeserializeJsonRpcNotification(json);
                return notification ?? throw new JsonException("Cannot parse JSON-RPC notification");
            }
        } else {
            var response = McpJsonSerializer.DeserializeJsonRpcResponse(json);
            return response ?? throw new JsonException("Cannot parse JSON-RPC response");
        }
    }

    /// <summary>获取响应标识的整数值。</summary>
    public static int GetIdAsInt(this JsonRpcResponse response) {
        if (response.Id.IsNumber)
            return (int)(response.Id.AsNumber ?? 0);
        return 0;
    }

    /// <summary>获取请求标识的整数值。</summary>
    public static int GetIdAsInt(this JsonRpcRequest request) {
        if (request.Id.IsNumber)
            return (int)(request.Id.AsNumber ?? 0);
        return 0;
    }

    /// <summary>反序列化响应结果为指定类型。</summary>
    public static T? DeserializeResult<T>(this JsonRpcResponse response, JsonTypeInfo<T> typeInfo) {
        if (response.Result is JsonElement element)
            return RelaxedJsonSerializer.Deserialize(element.GetRawText(), typeInfo);
        if (response.Result is null)
            return default;
        var json = McpJsonSerializer.SerializeObject(response.Result);
        return RelaxedJsonSerializer.Deserialize(json, typeInfo);
    }
}