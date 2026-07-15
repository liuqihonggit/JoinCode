
namespace McpClient;

internal static class McpMessageExtensions
{
    public static string ToJson(this JsonRpcMessage message)
    {
        return McpJsonSerializer.SerializeObject(message);
    }

    public static JsonRpcMessage FromJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj)
            throw new JsonException("Invalid JSON-RPC message");

        var hasId = obj.TryGetPropertyValue("id", out var idNode) && idNode is not null;
        var hasMethod = obj.ContainsKey("method");

        if (hasMethod)
        {
            if (hasId)
            {
                var request = McpJsonSerializer.DeserializeJsonRpcRequest(json);
                return request ?? throw new JsonException("Cannot parse JSON-RPC request");
            }
            else
            {
                var notification = McpJsonSerializer.DeserializeJsonRpcNotification(json);
                return notification ?? throw new JsonException("Cannot parse JSON-RPC notification");
            }
        }
        else
        {
            var response = McpJsonSerializer.DeserializeJsonRpcResponse(json);
            return response ?? throw new JsonException("Cannot parse JSON-RPC response");
        }
    }

    public static int GetIdAsInt(this JsonRpcResponse response)
    {
        if (response.Id.IsNumber)
            return (int)(response.Id.AsNumber ?? 0);
        return 0;
    }

    public static int GetIdAsInt(this JsonRpcRequest request)
    {
        if (request.Id.IsNumber)
            return (int)(request.Id.AsNumber ?? 0);
        return 0;
    }

    public static T? DeserializeResult<T>(this JsonRpcResponse response, JsonTypeInfo<T> typeInfo)
    {
        if (response.Result is JsonElement element)
            return JsonSerializer.Deserialize(element.GetRawText(), typeInfo);
        if (response.Result is null)
            return default;
        var json = McpJsonSerializer.SerializeObject(response.Result);
        return JsonSerializer.Deserialize(json, typeInfo);
    }
}
