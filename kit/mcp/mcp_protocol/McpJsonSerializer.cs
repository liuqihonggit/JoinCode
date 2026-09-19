global using McpProtocol.Contracts;

namespace McpProtocol;

/// <summary>
/// MCP JSON 序列化器 - NativeAOT 兼容实现
/// 使用源生成器，无运行时反射
/// </summary>
public static class McpJsonSerializer {
    #region Serialize 重载

    /// <summary>序列化 JSON-RPC 请求</summary>
    /// <param name="value">请求对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(JsonRpcRequest value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.JsonRpcRequest);

    /// <summary>序列化 JSON-RPC 响应</summary>
    /// <param name="value">响应对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(JsonRpcResponse value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.JsonRpcResponse);

    /// <summary>序列化 JSON-RPC 通知</summary>
    /// <param name="value">通知对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(JsonRpcNotification value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.JsonRpcNotification);

    /// <summary>序列化 tools/call 请求参数</summary>
    /// <param name="value">请求参数对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(CallToolRequestParams value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.CallToolRequestParams);

    /// <summary>序列化 resources/read 请求参数</summary>
    /// <param name="value">请求参数对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(McpResourceReadRequestParams value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.McpResourceReadRequestParams);

    /// <summary>序列化 prompts/get 请求参数</summary>
    /// <param name="value">请求参数对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(McpPromptGetRequestParams value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.McpPromptGetRequestParams);

    /// <summary>序列化 logging/setLevel 请求参数</summary>
    /// <param name="value">请求参数对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(LoggingSetLevelRequestParams value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.LoggingSetLevelRequestParams);

    /// <summary>序列化字符串到 JsonElement 的字典</summary>
    /// <param name="value">字典对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(Dictionary<string, JsonElement> value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.DictionaryStringJsonElement);

    /// <summary>序列化 tools/call 结果</summary>
    /// <param name="value">调用结果对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(CallToolResult value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.CallToolResult);

    /// <summary>序列化 tools/list 结果</summary>
    /// <param name="value">列表结果对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(ListToolsResult value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.ListToolsResult);

    /// <summary>序列化工具内容列表</summary>
    /// <param name="value">工具内容列表</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(List<ToolContent> value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.ListToolContent);

    /// <summary>序列化单个工具内容</summary>
    /// <param name="value">工具内容对象</param>
    /// <returns>JSON 字符串</returns>
    public static string Serialize(ToolContent value)
        => JsonSerializer.Serialize(value, McpJsonContext.Default.ToolContent);

    #endregion

    #region Deserialize 重载

    /// <summary>反序列化 JSON-RPC 请求(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>请求对象,解析失败返回 null</returns>
    public static JsonRpcRequest? DeserializeJsonRpcRequest(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcRequest);

    /// <summary>反序列化 JSON-RPC 响应(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>响应对象,解析失败返回 null</returns>
    public static JsonRpcResponse? DeserializeJsonRpcResponse(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcResponse);

    /// <summary>反序列化 JSON-RPC 通知(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>通知对象,解析失败返回 null</returns>
    public static JsonRpcNotification? DeserializeJsonRpcNotification(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.JsonRpcNotification);

    /// <summary>反序列化 tools/call 请求参数(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>请求参数对象,解析失败返回 null</returns>
    public static CallToolRequestParams? DeserializeCallToolRequestParams(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.CallToolRequestParams);

    /// <summary>反序列化 resources/read 请求参数(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>请求参数对象,解析失败返回 null</returns>
    public static McpResourceReadRequestParams? DeserializeMcpResourceReadRequestParams(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.McpResourceReadRequestParams);

    /// <summary>反序列化 prompts/get 请求参数(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>请求参数对象,解析失败返回 null</returns>
    public static McpPromptGetRequestParams? DeserializeMcpPromptGetRequestParams(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.McpPromptGetRequestParams);

    /// <summary>反序列化 logging/setLevel 请求参数(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>请求参数对象,解析失败返回 null</returns>
    public static LoggingSetLevelRequestParams? DeserializeLoggingSetLevelRequestParams(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.LoggingSetLevelRequestParams);

    /// <summary>反序列化字符串到 JsonElement 的字典(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>字典对象,解析失败返回 null</returns>
    public static Dictionary<string, JsonElement>? DeserializeDictionaryStringJsonElement(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.DictionaryStringJsonElement);

    /// <summary>反序列化 tools/call 结果(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>调用结果对象,解析失败返回 null</returns>
    public static CallToolResult? DeserializeCallToolResult(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.CallToolResult);

    /// <summary>反序列化 tools/list 结果(宽容解析)</summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>列表结果对象,解析失败返回 null</returns>
    public static ListToolsResult? DeserializeListToolsResult(string json)
        => RelaxedJsonSerializer.Deserialize(json, McpJsonContext.Default.ListToolsResult);

    #endregion

    #region Object 序列化 (非泛型)

    /// <summary>
    /// 非泛型序列化入口 — 根据运行时类型分派到对应的 JsonTypeInfo,
    /// 避免反射式序列化以保证 NativeAOT 兼容。
    /// </summary>
    /// <param name="value">待序列化对象</param>
    /// <returns>JSON 字符串,null 输入返回 "null"</returns>
    public static string SerializeObject(object value) {
        return SerializeObjectInternal(value);
    }

    private static string SerializeObjectInternal(object value) {
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