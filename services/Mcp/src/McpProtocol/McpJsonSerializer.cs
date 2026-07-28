global using McpProtocol.Contracts;

namespace McpProtocol;

/// <summary>
/// MCP JSON 序列化器 - NativeAOT 兼容实现
/// 使用源生成器，无运行时反射
/// </summary>
public static class McpJsonSerializer
{
    #region Serialize 重载

    public static string Serialize(JsonRpcRequest value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.JsonRpcRequest);

    public static string Serialize(JsonRpcResponse value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.JsonRpcResponse);

    public static string Serialize(JsonRpcNotification value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.JsonRpcNotification);

    public static string Serialize(CallToolRequestParams value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.CallToolRequestParams);

    public static string Serialize(McpResourceReadRequestParams value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.McpResourceReadRequestParams);

    public static string Serialize(McpPromptGetRequestParams value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.McpPromptGetRequestParams);

    public static string Serialize(LoggingSetLevelRequestParams value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.LoggingSetLevelRequestParams);

    public static string Serialize(Dictionary<string, JsonElement> value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.DictionaryStringJsonElement);

    public static string Serialize(CallToolResult value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.CallToolResult);

    public static string Serialize(ListToolsResult value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.ListToolsResult);

    public static string Serialize(List<ToolContent> value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.ListToolContent);

    public static string Serialize(ToolContent value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.ToolContent);

    #endregion

    #region Deserialize 重载

    public static JsonRpcRequest? DeserializeJsonRpcRequest(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcRequest);

    public static JsonRpcResponse? DeserializeJsonRpcResponse(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcResponse);

    public static JsonRpcNotification? DeserializeJsonRpcNotification(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcNotification);

    public static CallToolRequestParams? DeserializeCallToolRequestParams(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.CallToolRequestParams);

    public static McpResourceReadRequestParams? DeserializeMcpResourceReadRequestParams(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.McpResourceReadRequestParams);

    public static McpPromptGetRequestParams? DeserializeMcpPromptGetRequestParams(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.McpPromptGetRequestParams);

    public static LoggingSetLevelRequestParams? DeserializeLoggingSetLevelRequestParams(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.LoggingSetLevelRequestParams);

    public static Dictionary<string, JsonElement>? DeserializeDictionaryStringJsonElement(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.DictionaryStringJsonElement);

    public static CallToolResult? DeserializeCallToolResult(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.CallToolResult);

    public static ListToolsResult? DeserializeListToolsResult(string json)
        => JsonSerializer.Deserialize(json, McpJsonContext.Default.ListToolsResult);

    #endregion

    #region Object 序列化 (非泛型)

    public static string SerializeObject(object value)
    {
        return SerializeObjectInternal(value);
    }

    private static string SerializeObjectInternal(object value)
    {
        if (value is null) return "null";
        if (value is string s) return JsonSerializer.Serialize(s, McpJsonContext.Default.String);
        if (value is int i) return i.ToString();
        if (value is long l) return l.ToString();
        if (value is double d) return d.ToString();
        if (value is float f) return f.ToString();
        if (value is bool b) return b.ToString().ToLowerInvariant();
        if (value is JsonElement element) return element.GetRawText();
        if (value is Dictionary<string, JsonElement> dict) return JsonSerializer.Serialize(dict, McpJsonContext.Default.DictionaryStringJsonElement);
        if (value is List<ToolContent> list) return JsonSerializer.Serialize(list, McpJsonContext.Default.ListToolContent);
        if (value is ToolContent content) return JsonSerializer.Serialize(content, McpJsonContext.Default.ToolContent);
        if (value is CallToolResult result) return JsonSerializer.Serialize(result, McpJsonContext.Default.CallToolResult);
        if (value is ListToolsResult listResult) return JsonSerializer.Serialize(listResult, McpJsonContext.Default.ListToolsResult);
        // JSON-RPC 消息类型 — 必须用具体的 JsonTypeInfo 序列化，否则 ToString() 返回类型名
        if (value is JsonRpcRequest req) return JsonSerializer.Serialize(req, McpJsonContext.Default.JsonRpcRequest);
        if (value is JsonRpcResponse resp) return JsonSerializer.Serialize(resp, McpJsonContext.Default.JsonRpcResponse);
        if (value is JsonRpcNotification notif) return JsonSerializer.Serialize(notif, McpJsonContext.Default.JsonRpcNotification);

        return JsonSerializer.Serialize(value.ToString(), McpJsonContext.Default.String);
    }

    #endregion
}
